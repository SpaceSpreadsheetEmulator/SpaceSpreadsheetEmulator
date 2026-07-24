using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using SpaceSpreadsheetEmulator.Backplane.Contracts.V1;
using SpaceSpreadsheetEmulator.Gateway.Backplane;
using SpaceSpreadsheetEmulator.Protocol.MachoNet;
using SpaceSpreadsheetEmulator.Protocol.Profiles;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Gateway.Compatibility;

/// <summary>
/// Maps protocol-neutral solar-system output to build-3396210 notifications.
/// </summary>
internal static class Build3396210SpaceSessionMapper
{
    private const float StarterShipRadius = 39;
    private static readonly ProtocolProfile Profile =
        ProtocolProfileCatalog.GetRequired(3_396_210);

    public static MachoPacket CreateSnapshot(
        long proxyNodeId,
        long userId,
        CharacterSummary character,
        SolarSystemEntityState entity)
    {
        ValidateOwnShip(character, entity);
        uint stamp = ToStamp(entity.Tick);
        PyValue state = new PyObject(
            new PyBuffer("utillib.KeyVal"u8),
            Dictionary(
                ("damageState", new PyDictionary()),
                ("stamp", new PyInteger(stamp)),
                ("researchLevel", new PyInteger(0)),
                ("effectStates", new PyList()),
                ("industryLevel", new PyInteger(0)),
                ("state", new PyBuffer(CreateStoppedBallState(stamp, entity))),
                ("ego", new PyInteger(entity.ShipId)),
                ("slims", new PyList(CreateOwnShipSlim(character))),
                ("droneState", new PyList()),
                ("dbuffState", new PyList()),
                ("solItem", PyNull.Instance)));

        return CreateDestinyUpdate(
            proxyNodeId,
            userId,
            stamp,
            new PyTuple(new PyBuffer("SetState"u8), new PyTuple(state)));
    }

    public static MachoPacket CreateMovement(
        long proxyNodeId,
        long userId,
        SolarSystemEntityState entity)
    {
        uint stamp = ToStamp(entity.Tick);
        return CreateDestinyUpdate(
            proxyNodeId,
            userId,
            stamp,
            new PyTuple(
                new PyBuffer("SetBallPosition"u8),
                new PyTuple(
                    new PyInteger(entity.ShipId),
                    new PyFloat(entity.PositionX),
                    new PyFloat(entity.PositionY),
                    new PyFloat(entity.PositionZ))),
            new PyTuple(
                new PyBuffer("SetBallVelocity"u8),
                new PyTuple(
                    new PyInteger(entity.ShipId),
                    new PyFloat(entity.VelocityX),
                    new PyFloat(entity.VelocityY),
                    new PyFloat(entity.VelocityZ))));
    }

    public static MachoPacket CreateStop(
        long proxyNodeId,
        long userId,
        ulong tick,
        long shipId)
        => CreateDestinyUpdate(
            proxyNodeId,
            userId,
            ToStamp(tick),
            new PyTuple(
                new PyBuffer("Stop"u8),
                new PyTuple(new PyInteger(shipId))));

    public static MachoPacket CreateDockingAccepted(
        long proxyNodeId,
        long userId,
        int stationId)
        => MachoNotificationCodec.CreateServerBroadcast(
            "OnDockingAccepted",
            proxyNodeId,
            userId,
            new PyTuple(new PyInteger(stationId)),
            Profile);

    public static MachoPacket CreateObjectDisconnect(
        long proxyNodeId,
        long userId,
        string binding,
        long clientId)
        => MachoNotificationCodec.CreateServerBroadcast(
            "OnMachoObjectDisconnect",
            proxyNodeId,
            userId,
            new PyTuple(
                new PyText(binding),
                new PyInteger(clientId),
                PyNull.Instance),
            Profile,
            "+clientID");

    public static MachoPacket CreateDockingFinished(
        long proxyNodeId,
        long userId,
        int stationId)
        => MachoNotificationCodec.CreateServerBroadcast(
            "OnDockingFinished",
            proxyNodeId,
            userId,
            new PyTuple(new PyInteger(stationId)),
            Profile,
            "charid");

    private static MachoPacket CreateDestinyUpdate(
        long proxyNodeId,
        long userId,
        uint stamp,
        params PyTuple[] actions)
    {
        PyList updates = new(actions
            .Select(action => new PyTuple(new PyInteger(stamp), action))
            .ToArray());
        return MachoNotificationCodec.CreateServerBroadcast(
            "DoDestinyUpdate",
            proxyNodeId,
            userId,
            new PyTuple(updates, new PyBoolean(false), new PyList()),
            Profile);
    }

    private static PyObject CreateOwnShipSlim(CharacterSummary character)
        => new(
            new PyBuffer("eve.common.script.util.slimItem.SlimItem"u8),
            Dictionary(
                ("itemID", new PyInteger(character.ShipId)),
                ("typeID", new PyInteger(character.ShipTypeId)),
                ("groupID", new PyInteger(25)),
                ("categoryID", new PyInteger(6)),
                ("ownerID", new PyInteger(character.CharacterId)),
                ("charID", new PyInteger(character.CharacterId)),
                ("corpID", new PyInteger(character.CorporationId)),
                ("allianceID", PyNull.Instance),
                ("nameID", PyNull.Instance),
                ("name", new PyText(character.ShipName))));

    private static byte[] CreateStoppedBallState(
        uint stamp,
        SolarSystemEntityState entity)
    {
        var writer = new ArrayBufferWriter<byte>(44);
        WriteByte(writer, 0);
        WriteUInt32(writer, stamp);
        WriteInt64(writer, entity.ShipId);
        WriteByte(writer, 2);
        WriteSingle(writer, StarterShipRadius);
        WriteDouble(writer, entity.PositionX);
        WriteDouble(writer, entity.PositionY);
        WriteDouble(writer, entity.PositionZ);
        WriteByte(writer, 0);
        WriteByte(writer, byte.MaxValue);
        return writer.WrittenSpan.ToArray();
    }

    private static void ValidateOwnShip(
        CharacterSummary character,
        SolarSystemEntityState entity)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(entity);
        if (entity.CharacterId != character.CharacterId
            || entity.ShipId != character.ShipId
            || entity.SolarSystemId != character.SolarSystemId)
        {
            throw new InvalidDataException(
                "The initial solar-system snapshot does not contain the selected ship.");
        }

        if (character.ShipTypeId != 601)
        {
            throw new InvalidDataException(
                $"Ship type {character.ShipTypeId} has no build-3396210 presentation profile.");
        }
    }

    private static uint ToStamp(ulong tick)
        => checked((uint)tick);

    private static PyDictionary Dictionary(
        params (string Key, PyValue Value)[] entries)
        => new(entries.Select(
            entry => new PyDictionaryEntry(
                new PyBuffer(Encoding.UTF8.GetBytes(entry.Key)),
                entry.Value)).ToArray());

    private static void WriteByte(IBufferWriter<byte> writer, byte value)
    {
        Span<byte> destination = writer.GetSpan(1);
        destination[0] = value;
        writer.Advance(1);
    }

    private static void WriteUInt32(IBufferWriter<byte> writer, uint value)
    {
        Span<byte> destination = writer.GetSpan(sizeof(uint));
        BinaryPrimitives.WriteUInt32LittleEndian(destination, value);
        writer.Advance(sizeof(uint));
    }

    private static void WriteInt64(IBufferWriter<byte> writer, long value)
    {
        Span<byte> destination = writer.GetSpan(sizeof(long));
        BinaryPrimitives.WriteInt64LittleEndian(destination, value);
        writer.Advance(sizeof(long));
    }

    private static void WriteSingle(IBufferWriter<byte> writer, float value)
        => WriteInt32(writer, BitConverter.SingleToInt32Bits(value));

    private static void WriteDouble(IBufferWriter<byte> writer, double value)
        => WriteInt64(writer, BitConverter.DoubleToInt64Bits(value));

    private static void WriteInt32(IBufferWriter<byte> writer, int value)
    {
        Span<byte> destination = writer.GetSpan(sizeof(int));
        BinaryPrimitives.WriteInt32LittleEndian(destination, value);
        writer.Advance(sizeof(int));
    }
}
