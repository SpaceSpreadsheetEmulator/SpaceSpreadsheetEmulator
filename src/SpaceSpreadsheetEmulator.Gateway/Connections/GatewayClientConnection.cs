using System.Buffers;
using System.Collections.Immutable;
using System.Globalization;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using SpaceSpreadsheetEmulator.Backplane.Contracts.V1;
using SpaceSpreadsheetEmulator.Gateway.Backplane;
using SpaceSpreadsheetEmulator.Gateway.Compatibility;
using SpaceSpreadsheetEmulator.Protocol;
using SpaceSpreadsheetEmulator.Protocol.Codec;
using SpaceSpreadsheetEmulator.Protocol.Compression;
using SpaceSpreadsheetEmulator.Protocol.Crypto;
using SpaceSpreadsheetEmulator.Protocol.Framing;
using SpaceSpreadsheetEmulator.Protocol.Handshake;
using SpaceSpreadsheetEmulator.Protocol.MachoNet;
using SpaceSpreadsheetEmulator.Protocol.Profiles;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Gateway.Connections;

internal sealed partial class GatewayClientConnection(
    TcpClient client,
    ulong gatewaySessionId,
    IOptions<GatewayConnectionOptions> options,
    ILoginBackend loginBackend,
    ISolarSystemBackend solarSystemBackend,
    Build3396210StartupProfile startupProfile,
    TimeProvider timeProvider,
    ILogger<GatewayClientConnection> logger) : IDisposable
{
    private const long ProxyNodeId = 1;
    private readonly FrameReader frameReader = new();
    private readonly FrameWriter frameWriter = new();
    private readonly ProtocolProfile profile = ProtocolProfileCatalog.GetRequired(ProtocolProfileCatalog.SupportedBuild);
    private readonly ZlibPayloadCodec compression = new();
    private readonly CapturedStartupReplayCursor startupReplay = startupProfile.CreateCursor();
    private HandshakeState state = HandshakeState.WaitVersion;
    private AesCbcFrameCipher? cipher;
    private BackendLoginSession? loginSession;
    private CharacterSummary? selectedCharacter;
    private CharacterSelectionResponse? characterSelection;
    private string? solarSystemBinding;

    public void Dispose()
    {
        cipher?.Dispose();
        cipher = null;
        if (loginSession is not null)
        {
            CryptographicOperations.ZeroMemory(loginSession.LoginTicket);
            loginSession = null;
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (!await loginBackend.IsCompatibleAsync(cancellationToken))
        {
            LogBackendUnavailable(logger);
            return;
        }

        await using NetworkStream stream = client.GetStream();
        PipeReader input = PipeReader.Create(stream, new StreamPipeReaderOptions(leaveOpen: true));
        PipeWriter output = PipeWriter.Create(stream, new StreamPipeWriterOptions(leaveOpen: true));
        Channel<OutboundFrame> outbound = Channel.CreateBounded<OutboundFrame>(new BoundedChannelOptions(
            options.Value.OutboundQueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true,
        });

        Task writerTask = WriteFramesAsync(output, outbound.Reader, cancellationToken);
        try
        {
            await QueueValueAsync(HandshakeValueCodec.EncodeServerVersion(profile), outbound.Writer, encrypt: false, cancellationToken);
            await ReadFramesAsync(input, outbound.Writer, cancellationToken);
        }
        finally
        {
            outbound.Writer.TryComplete();
            await input.CompleteAsync();
            try
            {
                await writerTask;
            }
            finally
            {
                await output.CompleteAsync();
                if (loginSession is not null && !cancellationToken.IsCancellationRequested)
                {
                    await loginBackend.CloseSessionAsync(
                        gatewaySessionId,
                        loginSession.LoginTicket,
                        cancellationToken);
                }

                if (loginSession is not null)
                {
                    CryptographicOperations.ZeroMemory(loginSession.LoginTicket);
                }

                Dispose();
            }
        }
    }

    private async Task ReadFramesAsync(
        PipeReader input,
        ChannelWriter<OutboundFrame> outbound,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            ReadResult result = await input.ReadAsync(cancellationToken);
            ReadOnlySequence<byte> buffer = result.Buffer;
            FrameBatch batch = ParseFrames(buffer);
            input.AdvanceTo(batch.Consumed, buffer.End);

            if (batch.Error is not null)
            {
                LogProtocolError(batch.Error);
                return;
            }

            foreach (byte[] payload in batch.Payloads)
            {
                ProtocolError? error = await ProcessFrameAsync(payload, outbound, cancellationToken);
                if (error is not null)
                {
                    LogProtocolError(error);
                    return;
                }
            }

            if (result.IsCompleted)
            {
                if (buffer.Slice(batch.Consumed).Length != 0)
                {
                    LogIncompleteFrame(logger);
                }

                return;
            }
        }
    }

    private async Task<ProtocolError?> ProcessFrameAsync(
        byte[] wirePayload,
        ChannelWriter<OutboundFrame> outbound,
        CancellationToken cancellationToken)
    {
        byte[]? decrypted = null;
        try
        {
            ReadOnlyMemory<byte> plaintext = wirePayload;
            if (state >= HandshakeState.WaitAuthentication && cipher is not null)
            {
                DecodeResult<CipherPayload> decryption = cipher.Decrypt(wirePayload);
                if (!decryption.IsSuccess)
                {
                    return decryption.Error;
                }

                decrypted = decryption.Value!.Bytes;
                plaintext = decrypted;
            }

            DecodeResult<PyValue> decoded = DecodeValue(plaintext.Span);
            if (!decoded.IsSuccess)
            {
                return decoded.Error;
            }

            return state == HandshakeState.Authenticated
                ? await ProcessRpcAsync(decoded.Value!, outbound, cancellationToken)
                : await ProcessHandshakeAsync(decoded.Value!, outbound, cancellationToken);
        }
        finally
        {
            if (decrypted is not null)
            {
                CryptographicOperations.ZeroMemory(decrypted);
            }
        }
    }

    private async Task<ProtocolError?> ProcessHandshakeAsync(
        PyValue value,
        ChannelWriter<OutboundFrame> outbound,
        CancellationToken cancellationToken)
    {
        switch (state)
        {
            case HandshakeState.WaitVersion:
                {
                    DecodeResult<ClientVersionExchange> version = HandshakeValueCodec.DecodeClientVersion(value, profile);
                    if (!version.IsSuccess)
                    {
                        return version.Error;
                    }

                    state = HandshakeState.WaitCommand;
                    return null;
                }

            case HandshakeState.WaitCommand:
                {
                    DecodeResult<HandshakeCommand> command = HandshakeValueCodec.DecodeCommand(value);
                    if (!command.IsSuccess)
                    {
                        return command.Error;
                    }

                    if (command.Value!.Kind == HandshakeCommandKind.QueueCheck)
                    {
                        await QueueValueAsync(new PyInteger(0), outbound, encrypt: false, cancellationToken);
                        await QueueValueAsync(HandshakeValueCodec.EncodeServerVersion(profile), outbound, encrypt: false, cancellationToken);
                        state = HandshakeState.WaitVersion;
                    }
                    else
                    {
                        state = HandshakeState.WaitCrypto;
                    }

                    return null;
                }

            case HandshakeState.WaitCrypto:
                {
                    DecodeResult<CryptoRequest> crypto = HandshakeValueCodec.DecodeCryptoRequest(value);
                    if (!crypto.IsSuccess)
                    {
                        return crypto.Error;
                    }

                    if (!crypto.Value!.SessionKey.IsEmpty)
                    {
                        cipher = new AesCbcFrameCipher(
                            crypto.Value.SessionKey.AsSpan(),
                            crypto.Value.SessionInitializationVector.AsSpan());
                    }

                    await QueueValueAsync(new PyText("OK CC"), outbound, encrypt: false, cancellationToken);
                    state = HandshakeState.WaitAuthentication;
                    return null;
                }

            case HandshakeState.WaitAuthentication:
                {
                    DecodeResult<LoginCredentials> credentials = HandshakeValueCodec.DecodeLoginCredentials(value);
                    if (!credentials.IsSuccess)
                    {
                        return credentials.Error;
                    }

                    loginSession = await loginBackend.AuthenticateAsync(
                        gatewaySessionId,
                        credentials.Value!,
                        cancellationToken);
                    if (loginSession is null)
                    {
                        return Error(ProtocolErrorCodes.InvalidHandshake, "$handshake.login", "Authentication was rejected.");
                    }

                    long clientId = checked(1_000_000L + loginSession.AccountId);
                    await QueueValueAsync(HandshakeValueCodec.EncodePasswordVersion(), outbound, encrypt: cipher is not null, cancellationToken);
                    await QueueValueAsync(
                        HandshakeValueCodec.EncodeCryptoServerHandshake(profile, ProxyNodeId, clientId),
                        outbound,
                        encrypt: cipher is not null,
                        cancellationToken);
                    state = HandshakeState.WaitChallengeResult;
                    return null;
                }

            case HandshakeState.WaitChallengeResult:
                {
                    if (value is not PyTuple)
                    {
                        return Error(ProtocolErrorCodes.InvalidHandshake, "$handshake.challenge", "The challenge result is malformed.");
                    }

                    long clientId = checked(1_000_000L + loginSession!.AccountId);
                    await QueueValueAsync(
                        HandshakeValueCodec.EncodeSessionAcknowledgement(new SessionAcknowledgement(
                            loginSession.AccountId,
                            clientId,
                            checked((long)gatewaySessionId),
                            loginSession.Role,
                            loginSession.LanguageId,
                            loginSession.CountryCode,
                            client.Client.RemoteEndPoint?.ToString() ?? "loopback")),
                        outbound,
                        encrypt: cipher is not null,
                        cancellationToken);
                    await WritePacketAsync(
                        CreateInitialSessionNotification(clientId),
                        outbound,
                        cancellationToken);
                    state = HandshakeState.Authenticated;
                    LogHandshakeComplete(logger, loginSession.AccountId);
                    return null;
                }

            default:
                return Error(ProtocolErrorCodes.InvalidHandshake, "$handshake", "The handshake state is invalid.");
        }
    }

    private DecodeResult<PyValue> DecodeValue(ReadOnlySpan<byte> payload)
    {
        ReadOnlyMemory<byte> marshalPayload = payload.ToArray();
        if (LooksLikeZlib(payload))
        {
            DecodeResult<BinaryPayload> decompressed = compression.Decompress(payload);
            if (!decompressed.IsSuccess)
            {
                return DecodeResult<PyValue>.Failure(decompressed.Error!);
            }

            marshalPayload = decompressed.Value!.Bytes;
        }

        return BlueMarshalCodec.Decode(new ReadOnlySequence<byte>(marshalPayload), profile);
    }

    private FrameBatch ParseFrames(ReadOnlySequence<byte> buffer)
    {
        var reader = new SequenceReader<byte>(buffer);
        var payloads = new List<byte[]>();
        while (true)
        {
            FrameReadStatus status = frameReader.TryReadFrame(
                ref reader,
                out ReadOnlySequence<byte> payload,
                out ProtocolError? error);
            if (status == FrameReadStatus.Incomplete)
            {
                return new FrameBatch(reader.Position, payloads, null);
            }

            if (status == FrameReadStatus.Malformed)
            {
                return new FrameBatch(reader.Position, payloads, error);
            }

            payloads.Add(payload.ToArray());
        }
    }

    private async Task QueueValueAsync(
        PyValue value,
        ChannelWriter<OutboundFrame> outbound,
        bool encrypt,
        CancellationToken cancellationToken)
        => await outbound.WriteAsync(
            new OutboundFrame(BlueMarshalCodec.Encode(value, profile), encrypt),
            cancellationToken);

    private async Task WriteFramesAsync(
        PipeWriter output,
        ChannelReader<OutboundFrame> outbound,
        CancellationToken cancellationToken)
    {
        await foreach (OutboundFrame frame in outbound.ReadAllAsync(cancellationToken))
        {
            byte[] payload = frame.Encrypt
                ? cipher?.Encrypt(frame.Payload)
                    ?? throw new InvalidOperationException("An encrypted frame was queued before cipher initialization.")
                : frame.Payload;
            frameWriter.WriteFrame(payload, output);
            FlushResult flush = await output.FlushAsync(cancellationToken);
            if (flush.IsCanceled || flush.IsCompleted)
            {
                return;
            }
        }
    }

    private static bool LooksLikeZlib(ReadOnlySpan<byte> payload)
        => payload.Length >= 2
            && (payload[0] & 0x0F) == 8
            && ((payload[0] << 8) | payload[1]) % 31 == 0;

    private static PyDictionary Dictionary(params (string Key, PyValue Value)[] entries)
        => new(entries.Select(entry => new PyDictionaryEntry(new PyText(entry.Key), entry.Value)).ToArray());

    private static ProtocolError Error(string code, string path, string message)
        => new(code, 0, path, message);

    private void LogProtocolError(ProtocolError error)
        => LogMalformedConnection(logger, error.Code, error.ByteOffset, error.ValuePath, error.Message);

    [LoggerMessage(
        EventId = 100,
        Level = LogLevel.Warning,
        Message = "Closing malformed client connection: {ProtocolCode} at byte {ByteOffset}, path {ValuePath}: {ProtocolMessage}")]
    private static partial void LogMalformedConnection(
        ILogger logger,
        string protocolCode,
        long byteOffset,
        string valuePath,
        string protocolMessage);

    [LoggerMessage(EventId = 101, Level = LogLevel.Warning, Message = "Connection closed with an incomplete frame")]
    private static partial void LogIncompleteFrame(ILogger logger);

    [LoggerMessage(EventId = 102, Level = LogLevel.Warning, Message = "Login backend is unavailable or incompatible")]
    private static partial void LogBackendUnavailable(ILogger logger);

    [LoggerMessage(EventId = 103, Level = LogLevel.Information, Message = "Login handshake completed for account {AccountId}")]
    private static partial void LogHandshakeComplete(ILogger logger, long accountId);

    private sealed record FrameBatch(
        SequencePosition Consumed,
        IReadOnlyList<byte[]> Payloads,
        ProtocolError? Error);

    private enum HandshakeState
    {
        WaitVersion,
        WaitCommand,
        WaitCrypto,
        WaitAuthentication,
        WaitChallengeResult,
        Authenticated,
    }
}
