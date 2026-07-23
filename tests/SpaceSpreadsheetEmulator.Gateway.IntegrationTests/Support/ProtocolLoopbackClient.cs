using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Net.Sockets;
using System.Text;
using SpaceSpreadsheetEmulator.Protocol;
using SpaceSpreadsheetEmulator.Protocol.Codec;
using SpaceSpreadsheetEmulator.Protocol.Crypto;
using SpaceSpreadsheetEmulator.Protocol.Handshake;
using SpaceSpreadsheetEmulator.Protocol.MachoNet;
using SpaceSpreadsheetEmulator.Protocol.Profiles;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Gateway.IntegrationTests.Support;

internal sealed class ProtocolLoopbackClient(TcpClient client) : IDisposable
{
    private static readonly ProtocolProfile Profile = ProtocolProfileCatalog.GetRequired(3396210);
    private readonly NetworkStream stream = client.GetStream();
    private AesCbcFrameCipher? cipher;
    private long nextCallId = 100;

    public async Task<PyValue> ReadValueAsync(bool encrypted = false)
    {
        byte[] payload = await ReadPayloadAsync(encrypted);
        DecodeResult<PyValue> decoded = BlueMarshalCodec.Decode(new ReadOnlySequence<byte>(payload), Profile);
        Assert.True(decoded.IsSuccess, decoded.Error?.ToString());
        return decoded.Value!;
    }

    public Task WriteValueAsync(PyValue value, bool encrypted = false)
        => stream.WriteAsync(CreateFrame(BlueMarshalCodec.Encode(value, Profile, EncodingMode.Canonical), encrypted)).AsTask();

    public Task WritePacketAsync(MachoPacket packet, bool encrypted = true)
        => stream.WriteAsync(CreateFrame(MachoPacketCodec.Encode(packet, Profile), encrypted)).AsTask();

    public async Task<MachoPacket> ReadPacketAsync(bool encrypted = true)
    {
        byte[] payload = await ReadPayloadAsync(encrypted);
        DecodeResult<MachoPacket> decoded = MachoPacketCodec.Decode(new ReadOnlySequence<byte>(payload), Profile);
        Assert.True(decoded.IsSuccess, decoded.Error?.ToString());
        return decoded.Value!;
    }

    public async Task<PyValue> CallAsync(
        string? service,
        string method,
        PyTuple arguments,
        string? boundObject = null)
    {
        byte[] callBody = BlueMarshalCodec.Encode(
            new PyTuple(
                boundObject is null
                    ? new PyInteger(1)
                    : new PyBuffer(Encoding.UTF8.GetBytes(boundObject)),
                new PyBuffer(Encoding.UTF8.GetBytes(method)),
                arguments,
                new PyDictionary()),
            Profile);
        long callId = Interlocked.Increment(ref nextCallId);
        var request = new MachoPacket(
            "carbon.common.script.net.machoNetPacket.CallReq",
            6,
            new MachoClientAddress(0, callId),
            service is null ? MachoAnyAddress.Instance : new MachoServiceAddress(service),
            null,
            new PyTuple(new PyTuple(new PyInteger(boundObject is null ? 0 : 1), new PySubstream(ImmutableArray.Create(callBody)))),
            Enumerable.Repeat<PyValue>(PyNull.Instance, 9).ToImmutableArray());

        await WritePacketAsync(request);
        MachoPacket response = await ReadPacketAsync();
        Assert.Equal(7, response.NumericType);
        Assert.Equal(callId, Assert.IsType<MachoClientAddress>(response.Destination).CallId);
        PySubstream result = Assert.IsType<PySubstream>(Assert.IsType<PyTuple>(response.Payload).Items[0]);
        DecodeResult<PyValue> decoded = BlueMarshalCodec.Decode(new ReadOnlySequence<byte>(result.Data.AsMemory()), Profile);
        Assert.True(decoded.IsSuccess, decoded.Error?.ToString());
        return decoded.Value!;
    }

    public async Task WriteFragmentedAsync(PyValue value)
    {
        byte[] frame = CreateFrame(BlueMarshalCodec.Encode(value, Profile, EncodingMode.Canonical), encrypted: false);
        foreach (byte item in frame)
        {
            await stream.WriteAsync(new[] { item });
        }
    }

    public Task WriteCoalescedAsync(params PyValue[] values)
    {
        byte[] bytes = values.SelectMany(value => CreateFrame(
            BlueMarshalCodec.Encode(value, Profile, EncodingMode.Canonical),
            encrypted: false)).ToArray();
        return stream.WriteAsync(bytes).AsTask();
    }

    public async Task CompleteHandshakeAsync(bool fragmentVersion = false)
    {
        Assert.IsType<PyTuple>(await ReadValueAsync());
        PyValue version = HandshakeValueCodec.EncodeServerVersion(Profile);
        if (fragmentVersion)
        {
            await WriteFragmentedAsync(version);
        }
        else
        {
            await WriteValueAsync(version);
        }

        byte[] versionKey = [0x10, 0x20, 0x30, 0x40];
        byte[] key = Enumerable.Range(0, AesCbcFrameCipher.KeyLength).Select(value => (byte)value).ToArray();
        byte[] iv = Enumerable.Range(0x40, AesCbcFrameCipher.BlockLength).Select(value => (byte)value).ToArray();
        await WriteCoalescedAsync(
            new PyTuple(new PyInteger(0), new PyText("VK"), new PyBuffer(versionKey.ToImmutableArray())),
            new PyTuple(
                new PyText("placebo"),
                Dictionary(
                    ("crypting_sessionkey", new PyBuffer(key.ToImmutableArray())),
                    ("crypting_sessioniv", new PyBuffer(iv.ToImmutableArray())))));
        Assert.Equal("OK CC", Assert.IsType<PyText>(await ReadValueAsync()).Value);

        cipher = new AesCbcFrameCipher(key, iv);
        await WriteValueAsync(
            new PyTuple(
                new PyInteger(0),
                Dictionary(
                    ("user_name", new PyText("integration-pilot")),
                    ("user_password_hash", new PyBuffer(ImmutableArray.Create<byte>(0xCA, 0xFE))),
                    ("user_languageid", new PyText("EN")),
                    ("user_countrycode", new PyText("BG")))),
            encrypted: true);
        Assert.Equal(2, Assert.IsType<PyInteger>(await ReadValueAsync(encrypted: true)).Value);
        Assert.IsType<PyTuple>(await ReadValueAsync(encrypted: true));

        await WriteValueAsync(new PyTuple(), encrypted: true);
        Assert.IsType<PyDictionary>(await ReadValueAsync(encrypted: true));
        MachoPacket initialSession = await ReadPacketAsync(encrypted: true);
        Assert.Equal(16, initialSession.NumericType);
        Assert.IsType<PyTuple>(initialSession.Payload);
    }

    public void Dispose()
    {
        cipher?.Dispose();
        client.Dispose();
    }

    private async Task<byte[]> ReadPayloadAsync(bool encrypted)
    {
        byte[] header = await LoopbackClient.ReadExactlyAsync(stream, sizeof(int));
        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        Assert.InRange(length, 1, 16 * 1024 * 1024);
        byte[] payload = await LoopbackClient.ReadExactlyAsync(stream, length);
        if (encrypted)
        {
            DecodeResult<CipherPayload> decrypted = cipher!.Decrypt(payload);
            Assert.True(decrypted.IsSuccess, decrypted.Error?.ToString());
            payload = decrypted.Value!.Bytes;
        }

        return payload;
    }

    private byte[] CreateFrame(byte[] payload, bool encrypted)
    {
        if (encrypted)
        {
            payload = cipher!.Encrypt(payload);
        }

        byte[] frame = new byte[sizeof(int) + payload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, payload.Length);
        payload.CopyTo(frame, sizeof(int));
        return frame;
    }

    private static PyDictionary Dictionary(params (string Key, PyValue Value)[] entries)
        => new(entries.Select(entry => new PyDictionaryEntry(new PyText(entry.Key), entry.Value)).ToArray());
}
