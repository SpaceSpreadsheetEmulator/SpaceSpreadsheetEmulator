using System.Buffers;
using System.Collections.Immutable;
using System.Text;
using SpaceSpreadsheetEmulator.Protocol.Codec;
using SpaceSpreadsheetEmulator.Protocol.Profiles;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Protocol.MachoNet;

public static class MachoPacketCodec
{
    private const string AddressTypeName = "carbon.common.script.net.machoNetAddress.MachoAddress";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static DecodeResult<MachoPacket> Decode(ReadOnlySequence<byte> input, ProtocolProfile profile)
    {
        DecodeResult<PyValue> decoded = BlueMarshalCodec.Decode(input, profile);
        if (!decoded.IsSuccess)
        {
            return DecodeResult<MachoPacket>.Failure(decoded.Error!);
        }

        if (decoded.Value is not PyObject root || root.State is not PyTuple state || state.Items.Length != 14)
        {
            return Failure("The root value is not a valid 14-field MachoNet packet object.");
        }

        if (!TryReadString(root.Type, out string? objectTypeName))
        {
            return Failure("The MachoNet packet object type is not a string value.");
        }

        if (!TryReadInt64(state.Items[0], out long packetType) || packetType is < int.MinValue or > int.MaxValue)
        {
            return Failure("The MachoNet packet type is not a 32-bit integer.");
        }

        if (!TryDecodeAddress(state.Items[1], out MachoAddress? source))
        {
            return Failure("The MachoNet source address is malformed.");
        }

        if (!TryDecodeAddress(state.Items[2], out MachoAddress? destination))
        {
            return Failure("The MachoNet destination address is malformed.");
        }

        if (!TryDecodeNullableInt64(state.Items[3], out long? userId))
        {
            return Failure("The MachoNet user identifier is malformed.");
        }

        return DecodeResult<MachoPacket>.Success(new MachoPacket(
            objectTypeName!,
            (int)packetType,
            source!,
            destination!,
            userId,
            state.Items[4],
            state.Items[5..])
        {
            OriginalValue = root,
        });
    }

    public static void Encode(
        MachoPacket packet,
        IBufferWriter<byte> output,
        ProtocolProfile profile,
        EncodingMode mode = EncodingMode.Canonical)
    {
        ArgumentNullException.ThrowIfNull(packet);
        if (mode == EncodingMode.PreserveWireForm && packet.OriginalValue is not null)
        {
            BlueMarshalCodec.Encode(packet.OriginalValue, output, profile, mode);
            return;
        }

        if (packet.Extensions.Length != 9)
        {
            throw new ArgumentException("A MachoNet packet requires exactly nine ordered extension fields.", nameof(packet));
        }

        var fields = ImmutableArray.CreateBuilder<PyValue>(14);
        fields.Add(new PyInteger(packet.NumericType));
        fields.Add(EncodeAddress(packet.Source));
        fields.Add(EncodeAddress(packet.Destination));
        fields.Add(packet.UserId is long userId ? new PyInteger(userId) : PyNull.Instance);
        fields.Add(packet.Payload);
        fields.AddRange(packet.Extensions);

        var value = new PyObject(
            new PyBuffer(Encoding.ASCII.GetBytes(packet.ObjectTypeName)),
            new PyTuple(fields.MoveToImmutable()));
        BlueMarshalCodec.Encode(value, output, profile, mode);
    }

    public static byte[] Encode(MachoPacket packet, ProtocolProfile profile, EncodingMode mode = EncodingMode.Canonical)
    {
        var output = new ArrayBufferWriter<byte>();
        Encode(packet, output, profile, mode);
        return output.WrittenSpan.ToArray();
    }

    private static PyValue EncodeAddress(MachoAddress address)
    {
        if (address.OriginalValue is not null)
        {
            return address.OriginalValue;
        }

        PyTuple arguments = address switch
        {
            MachoNodeAddress node => new PyTuple(
                new PyInteger(1),
                node.NodeId is long nodeId ? new PyInteger(nodeId) : PyNull.Instance,
                StringOrNull(node.Service),
                PyNull.Instance),
            MachoClientAddress client => new PyTuple(
                new PyInteger(2),
                new PyInteger(client.ClientId),
                client.CallId is long callId ? new PyInteger(callId) : PyNull.Instance,
                PyNull.Instance),
            MachoBroadcastAddress broadcast => new PyTuple(
                new PyInteger(4),
                new PyBuffer(Encoding.UTF8.GetBytes(broadcast.Scope)),
                broadcast.Narrowcast,
                StringOrNull(broadcast.Service)),
            MachoAnyAddress => new PyTuple(new PyInteger(8), PyNull.Instance, PyNull.Instance),
            MachoServiceAddress service => new PyTuple(
                new PyInteger(8),
                new PyBuffer(Encoding.UTF8.GetBytes(service.Service)),
                PyNull.Instance),
            _ => throw new ArgumentException($"Unknown MachoNet address {address.GetType().Name}.", nameof(address)),
        };

        return new PyObject(new PyBuffer(Encoding.ASCII.GetBytes(AddressTypeName)), arguments);
    }

    private static PyValue StringOrNull(string? value)
        => value is null ? PyNull.Instance : new PyBuffer(Encoding.UTF8.GetBytes(value));

    private static bool TryDecodeAddress(PyValue value, out MachoAddress? address)
    {
        address = null;
        if (value is not PyObject { State: PyTuple arguments } root
            || !TryReadString(root.Type, out string? typeName)
            || typeName != AddressTypeName
            || arguments.Items.Length is < 3 or > 4
            || !TryReadInt64(arguments.Items[0], out long kind))
        {
            return false;
        }

        switch (kind)
        {
            case 1 when arguments.Items.Length == 4
                && TryDecodeNullableInt64(arguments.Items[1], out long? nodeId)
                && TryReadNullableString(arguments.Items[2], out string? nodeService):
                address = new MachoNodeAddress(nodeId, nodeService) { OriginalValue = root };
                return true;
            case 2 when arguments.Items.Length == 4
                && TryReadInt64(arguments.Items[1], out long clientId)
                && TryDecodeNullableInt64(arguments.Items[2], out long? callId):
                address = new MachoClientAddress(clientId, callId) { OriginalValue = root };
                return true;
            case 4 when arguments.Items.Length == 4
                && TryReadString(arguments.Items[1], out string? scope)
                && TryReadNullableString(arguments.Items[3], out string? broadcastService):
                address = new MachoBroadcastAddress(scope!, arguments.Items[2], broadcastService) { OriginalValue = root };
                return true;
            case 8 when arguments.Items.Length == 3
                && TryReadNullableString(arguments.Items[1], out string? service):
                address = service is null
                    ? MachoAnyAddress.Instance with { OriginalValue = root }
                    : new MachoServiceAddress(service) { OriginalValue = root };
                return true;
            default:
                return false;
        }
    }

    private static bool TryDecodeNullableInt64(PyValue value, out long? result)
    {
        if (value is PyNull)
        {
            result = null;
            return true;
        }

        if (TryReadInt64(value, out long integer))
        {
            result = integer;
            return true;
        }

        result = null;
        return false;
    }

    private static bool TryReadInt64(PyValue value, out long result)
    {
        value = value is PySavedValueReference reference ? reference.Value : value;
        if (value is PyInteger integer)
        {
            result = integer.Value;
            return true;
        }

        if (value is PyBigInteger bigInteger && bigInteger.Value >= long.MinValue && bigInteger.Value <= long.MaxValue)
        {
            result = (long)bigInteger.Value;
            return true;
        }

        result = 0;
        return false;
    }

    private static bool TryReadNullableString(PyValue value, out string? result)
    {
        if (value is PyNull)
        {
            result = null;
            return true;
        }

        return TryReadString(value, out result);
    }

    private static bool TryReadString(PyValue value, out string? result)
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
            case PyStringTableReference stringReference:
                result = stringReference.Value;
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

    private static DecodeResult<MachoPacket> Failure(string message)
        => DecodeResult<MachoPacket>.Failure(new ProtocolError(
            ProtocolErrorCodes.InvalidValue,
            0,
            "$packet",
            message));
}
