using System.Buffers;
using System.Collections.Immutable;
using System.Text;
using SpaceSpreadsheetEmulator.Protocol.Codec;
using SpaceSpreadsheetEmulator.Protocol.Profiles;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Protocol.MachoNet;

/// <summary>
/// Represents a decoded MachoNet service or bound-object call request.
/// </summary>
public sealed record MachoRpcRequest(
    MachoPacket Packet,
    string? Service,
    string? BoundObject,
    string Method,
    long CallId,
    PyTuple Arguments,
    PyDictionary KeywordArguments);

/// <summary>
/// Decodes MachoNet call envelopes and creates their matching response packets.
/// </summary>
public static class MachoRpcCodec
{
    private const int CallRequestType = 6;
    private const int CallResponseType = 7;
    private const string CallResponseObjectType = "carbon.common.script.net.machoNetPacket.CallRsp";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static DecodeResult<MachoRpcRequest> DecodeRequest(MachoPacket packet, ProtocolProfile profile)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentNullException.ThrowIfNull(profile);
        string? service = packet.Destination switch
        {
            MachoServiceAddress serviceAddress => serviceAddress.Service,
            MachoNodeAddress nodeAddress => nodeAddress.Service,
            _ => null,
        };
        if (packet.NumericType != CallRequestType
            || packet.Source is not MachoClientAddress { CallId: long callId }
            || packet.Payload is not PyTuple { Items.Length: 1 } payload
            || payload.Items[0] is not PyTuple { Items.Length: 2 } callEnvelope
            || callEnvelope.Items[1] is not PySubstream substream)
        {
            return Failure("The MachoNet call request envelope is malformed.");
        }

        DecodeResult<PyValue> decoded = BlueMarshalCodec.Decode(
            new ReadOnlySequence<byte>(substream.Data.AsMemory()),
            profile);
        if (!decoded.IsSuccess)
        {
            return DecodeResult<MachoRpcRequest>.Failure(decoded.Error!);
        }

        if (decoded.Value is not PyTuple { Items.Length: 4 } call
            || !TryString(call.Items[1], out string? method)
            || call.Items[2] is not PyTuple arguments
            || call.Items[3] is not PyDictionary keywordArguments)
        {
            return Failure("The MachoNet call request body is malformed.");
        }

        string? boundObject = TryString(call.Items[0], out string? objectIdentifier)
            && objectIdentifier!.StartsWith("N=", StringComparison.Ordinal)
                ? objectIdentifier
                : null;
        if (string.IsNullOrWhiteSpace(service) && boundObject is null)
        {
            return Failure("The MachoNet call request has neither a service nor a bound object.");
        }

        return DecodeResult<MachoRpcRequest>.Success(new MachoRpcRequest(
            packet,
            service,
            boundObject,
            method!,
            callId,
            arguments,
            keywordArguments));
    }

    public static MachoPacket CreateResponse(
        MachoRpcRequest request,
        long clientId,
        long userId,
        PyValue result,
        ProtocolProfile profile)
    {
        byte[] resultBytes = BlueMarshalCodec.Encode(result, profile);
        ImmutableArray<PyValue> requestExtensions = request.Packet.Extensions;
        ImmutableArray<PyValue> extensions =
        [
            requestExtensions.ElementAtOrDefault(0) ?? PyNull.Instance,
            requestExtensions.ElementAtOrDefault(1) ?? PyNull.Instance,
            requestExtensions.ElementAtOrDefault(2) ?? PyNull.Instance,
            PyNull.Instance,
            PyNull.Instance,
            new PyBoolean(false),
            new PyInteger(0),
            new PyInteger(1000),
            PyNull.Instance,
        ];
        return new MachoPacket(
            CallResponseObjectType,
            CallResponseType,
            request.Packet.Destination,
            new MachoClientAddress(clientId, request.CallId),
            userId,
            new PyTuple(new PySubstream(ImmutableArray.Create(resultBytes))),
            extensions);
    }

    private static bool TryString(PyValue value, out string? result)
    {
        value = value is PySavedValueReference reference ? reference.Value : value;
        switch (value)
        {
            case PyText text:
                result = text.Value;
                return true;
            case PyToken token:
                result = token.Value;
                return true;
            case PyStringTableReference tableReference:
                result = tableReference.Value;
                return true;
            case PyBuffer buffer:
                try
                {
                    result = StrictUtf8.GetString(buffer.Value.AsSpan());
                    return true;
                }
                catch (DecoderFallbackException)
                {
                    break;
                }
        }

        result = null;
        return false;
    }

    private static DecodeResult<MachoRpcRequest> Failure(string message)
        => DecodeResult<MachoRpcRequest>.Failure(new ProtocolError(
            ProtocolErrorCodes.InvalidValue,
            0,
            "$rpc",
            message));
}
