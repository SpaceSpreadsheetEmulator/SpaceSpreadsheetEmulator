using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Net.Sockets;
using System.Text;
using SpaceSpreadsheetEmulator.Protocol;
using SpaceSpreadsheetEmulator.Protocol.Codec;
using SpaceSpreadsheetEmulator.Protocol.Compression;
using SpaceSpreadsheetEmulator.Protocol.Crypto;
using SpaceSpreadsheetEmulator.Protocol.Handshake;
using SpaceSpreadsheetEmulator.Protocol.MachoNet;
using SpaceSpreadsheetEmulator.Protocol.Profiles;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Gateway.IntegrationTests.Support;

internal sealed class ProtocolLoopbackClient(TcpClient client) : IDisposable
{
    private static readonly ProtocolProfile Profile = ProtocolProfileCatalog.GetRequired(3396210);
    private static readonly ZlibPayloadCodec Compression = new(Profile.Limits);
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
        string? boundObject = null,
        PyDictionary? keywordArguments = null)
    {
        long callId = await WriteCallAsync(
            service,
            method,
            arguments,
            boundObject,
            keywordArguments: keywordArguments);
        return await ReadCallResponseAsync(callId);
    }

    public async Task<PyValue> CallCachedMethodAsync(
        string service,
        string method,
        PyTuple arguments)
    {
        PyObject methodResult = Assert.IsType<PyObject>(await CallAsync(service, method, arguments));
        PyTuple methodState = Assert.IsType<PyTuple>(methodResult.State);
        PyObject cacheReference = Assert.IsType<PyObject>(methodState.Items[1]);
        PyTuple referenceState = Assert.IsType<PyTuple>(cacheReference.State);
        PyValue key = referenceState.Items[0];
        PyValue nodeId = referenceState.Items[1];
        PyValue version = referenceState.Items[2];

        PyObject cachedObject = Assert.IsType<PyObject>(await CallAsync(
            "objectCaching",
            "GetCachableObject",
            new PyTuple(new PyInteger(1), key, version, nodeId)));
        PyTuple cachedState = Assert.IsType<PyTuple>(cachedObject.State);
        Assert.Equal(7, cachedState.Items.Length);
        Assert.IsType<PyNull>(cachedState.Items[1]);
        Assert.Equal(1, Assert.IsType<PyInteger>(cachedState.Items[3]).Value);
        PyBuffer payload = Assert.IsType<PyBuffer>(cachedState.Items[4]);
        Assert.Equal(1, Assert.IsType<PyInteger>(cachedState.Items[5]).Value);
        Assert.Equal(
            BlueMarshalCodec.Encode(key, Profile),
            BlueMarshalCodec.Encode(cachedState.Items[6], Profile));
        DecodeResult<BinaryPayload> decompressed = Compression.Decompress(payload.Value.AsSpan());
        Assert.True(decompressed.IsSuccess, decompressed.Error?.ToString());
        DecodeResult<PyValue> decoded = BlueMarshalCodec.Decode(
            new ReadOnlySequence<byte>(decompressed.Value!.Bytes),
            Profile);
        Assert.True(decoded.IsSuccess, decoded.Error?.ToString());
        return decoded.Value!;
    }

    public async Task<long> WriteCallAsync(
        string? service,
        string method,
        PyTuple arguments,
        string? boundObject = null,
        ImmutableArray<PyValue> extensions = default,
        PyDictionary? keywordArguments = null)
    {
        byte[] callBody = BlueMarshalCodec.Encode(
            new PyTuple(
                boundObject is null
                    ? new PyInteger(1)
                    : new PyBuffer(Encoding.UTF8.GetBytes(boundObject)),
                new PyBuffer(Encoding.UTF8.GetBytes(method)),
                arguments,
                keywordArguments ?? new PyDictionary()),
            Profile);
        return await WriteEncodedCallAsync(service, callBody, boundObject, extensions);
    }

    public async Task<long> WriteEncodedCallAsync(
        string? service,
        byte[] callBody,
        string? boundObject = null,
        ImmutableArray<PyValue> extensions = default)
    {
        ArgumentNullException.ThrowIfNull(callBody);
        if (callBody.Length == 0)
        {
            throw new ArgumentException("An encoded call body is required.", nameof(callBody));
        }

        long callId = Interlocked.Increment(ref nextCallId);
        var request = new MachoPacket(
            "carbon.common.script.net.machoNetPacket.CallReq",
            6,
            new MachoClientAddress(0, callId),
            service is null ? MachoAnyAddress.Instance : new MachoServiceAddress(service),
            null,
            new PyTuple(new PyTuple(new PyInteger(boundObject is null ? 0 : 1), new PySubstream(ImmutableArray.Create(callBody)))),
            extensions.IsDefault
                ? Enumerable.Repeat<PyValue>(PyNull.Instance, 9).ToImmutableArray()
                : extensions);

        await WritePacketAsync(request);
        return callId;
    }

    public async Task<PyValue> ReadCallResponseAsync(long callId)
    {
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

    public Task CompleteHandshakeAsync(bool fragmentVersion = false)
        => CompleteHandshakeAsync(
            "integration-pilot",
            ImmutableArray.Create<byte>(0xCA, 0xFE),
            fragmentVersion);

    public async Task CompleteHandshakeAsync(
        string userName,
        ImmutableArray<byte> credentialProof,
        bool fragmentVersion = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        if (credentialProof.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A credential proof is required.", nameof(credentialProof));
        }

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
                    ("user_name", new PyText(userName)),
                    ("user_password_hash", new PyBuffer(credentialProof)),
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

        if (LooksLikeZlib(payload))
        {
            DecodeResult<BinaryPayload> decompressed = Compression.Decompress(payload);
            Assert.True(decompressed.IsSuccess, decompressed.Error?.ToString());
            payload = decompressed.Value!.Bytes;
        }

        return payload;
    }

    private static bool LooksLikeZlib(ReadOnlySpan<byte> payload)
        => payload.Length >= 2
            && (payload[0] & 0x0F) == 8
            && ((payload[0] << 8) | payload[1]) % 31 == 0;

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
