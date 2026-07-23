using System.Buffers;
using System.Text;
using SpaceSpreadsheetEmulator.Protocol.Codec;
using SpaceSpreadsheetEmulator.Protocol.Profiles;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Protocol.MachoNet;

/// <summary>
/// Represents one decoded client-to-node MachoNet notification.
/// </summary>
public sealed record MachoClientNotification(
    MachoPacket Packet,
    string Method,
    PyTuple Arguments,
    PyDictionary KeywordArguments);

/// <summary>
/// Decodes the nested body carried by a client-to-node MachoNet notification.
/// </summary>
public static class MachoNotificationCodec
{
    private const int NotificationType = 12;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static DecodeResult<MachoClientNotification> DecodeClientNotification(
        MachoPacket packet,
        ProtocolProfile profile)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentNullException.ThrowIfNull(profile);
        if (packet.NumericType != NotificationType
            || packet.Source is not MachoNodeAddress
            || packet.Destination is not MachoNodeAddress
            || packet.Payload is not PyTuple { Items.Length: 1 } payload
            || payload.Items[0] is not PyTuple { Items.Length: 2 } envelope
            || envelope.Items[1] is not PySubstream substream)
        {
            return Failure("The MachoNet client notification envelope is malformed.");
        }

        DecodeResult<PyValue> decoded = BlueMarshalCodec.Decode(
            new ReadOnlySequence<byte>(substream.Data.AsMemory()),
            profile);
        if (!decoded.IsSuccess)
        {
            return DecodeResult<MachoClientNotification>.Failure(decoded.Error!);
        }

        if (decoded.Value is not PyTuple { Items.Length: 4 } notification
            || !TryString(notification.Items[1], out string? method)
            || notification.Items[2] is not PyTuple arguments
            || notification.Items[3] is not PyDictionary keywordArguments)
        {
            return Failure("The MachoNet client notification body is malformed.");
        }

        return DecodeResult<MachoClientNotification>.Success(new MachoClientNotification(
            packet,
            method!,
            arguments,
            keywordArguments));
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

    private static DecodeResult<MachoClientNotification> Failure(string message)
        => DecodeResult<MachoClientNotification>.Failure(new ProtocolError(
            ProtocolErrorCodes.InvalidValue,
            0,
            "$notification",
            message));
}
