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
        SolarSystemEntityState entity,
        IReadOnlyList<SolarSystemStaticObjectState> staticObjects,
        DateTimeOffset observedAt)
    {
        ValidateOwnShip(character, entity);
        ValidateStaticObjects(character, entity, staticObjects);
        uint stamp = ToStamp(entity.Tick);
        PyValue[] slims =
        [
            CreateOwnShipSlim(character),
            .. staticObjects
                .OrderBy(item => item.EntityId)
                .Select(CreateStaticObjectSlim),
        ];
        PyValue state = new PyObject(
            new PyBuffer("utillib.KeyVal"u8),
            Dictionary(
                ("damageState", new PyDictionary(new PyDictionaryEntry(
                    new PyInteger(entity.ShipId),
                    CreateFullDamageState(observedAt)))),
                ("stamp", new PyInteger(stamp)),
                ("researchLevel", new PyInteger(0)),
                ("effectStates", new PyList()),
                ("industryLevel", new PyInteger(0)),
                ("state", new PyBuffer(CreateBallState(stamp, entity, staticObjects))),
                ("ego", new PyInteger(entity.ShipId)),
                ("slims", new PyList(slims)),
                ("droneState", new PyList()),
                ("dbuffState", new PyList()),
                ("aggressors", new PyDictionary()),
                ("allianceBridges", new PyList()),
                ("solItem", Build3396210InventoryMapper.CreateSolarSystem(character))));

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

    private static PyObject CreateStaticObjectSlim(SolarSystemStaticObjectState item)
    {
        (int groupId, int categoryId) = item.Kind switch
        {
            SolarSystemStaticObjectKind.Station => (15, 3),
            SolarSystemStaticObjectKind.Planet => (7, 2),
            SolarSystemStaticObjectKind.JumpGate => (10, 2),
            _ => throw new InvalidDataException(
                $"Static object {item.EntityId} has unsupported kind {item.Kind}."),
        };
        return new PyObject(
            new PyBuffer("eve.common.script.util.slimItem.SlimItem"u8),
            Dictionary(
                ("itemID", new PyInteger(item.EntityId)),
                ("typeID", new PyInteger(item.TypeId)),
                ("groupID", new PyInteger(groupId)),
                ("categoryID", new PyInteger(categoryId)),
                ("ownerID", new PyInteger(item.OwnerId)),
                ("nameID", PyNull.Instance),
                ("name", new PyText(item.Name))));
    }

    private static PyList CreateFullDamageState(DateTimeOffset observedAt)
        => new(
            new PyTuple(
                new PyFloat(1),
                new PyFloat(0),
                new PyBigInteger(observedAt.UtcDateTime.ToFileTimeUtc())),
            new PyFloat(1),
            new PyFloat(1));

    private static byte[] CreateBallState(
        uint stamp,
        SolarSystemEntityState entity,
        IReadOnlyList<SolarSystemStaticObjectState> staticObjects)
    {
        var writer = new ArrayBufferWriter<byte>(
            5 + (39 * checked(staticObjects.Count + 1)));
        WriteByte(writer, 0);
        WriteUInt32(writer, stamp);
        WriteOwnShipBall(writer, entity);
        foreach (SolarSystemStaticObjectState item in staticObjects.OrderBy(item => item.EntityId))
        {
            WriteStaticObjectBall(writer, item);
        }

        return writer.WrittenSpan.ToArray();
    }

    private static void WriteOwnShipBall(
        IBufferWriter<byte> writer,
        SolarSystemEntityState entity)
    {
        WriteInt64(writer, entity.ShipId);
        WriteByte(writer, 2);
        WriteSingle(writer, StarterShipRadius);
        WriteDouble(writer, entity.PositionX);
        WriteDouble(writer, entity.PositionY);
        WriteDouble(writer, entity.PositionZ);
        WriteByte(writer, 0);
        WriteByte(writer, byte.MaxValue);
    }

    private static void WriteStaticObjectBall(
        IBufferWriter<byte> writer,
        SolarSystemStaticObjectState item)
    {
        WriteInt64(writer, item.EntityId);
        WriteByte(writer, 11);
        WriteSingle(writer, checked((float)item.Radius));
        WriteDouble(writer, item.PositionX);
        WriteDouble(writer, item.PositionY);
        WriteDouble(writer, item.PositionZ);
        WriteByte(writer, 6);
        WriteByte(writer, byte.MaxValue);
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

    private static void ValidateStaticObjects(
        CharacterSummary character,
        SolarSystemEntityState entity,
        IReadOnlyList<SolarSystemStaticObjectState> staticObjects)
    {
        ArgumentNullException.ThrowIfNull(staticObjects);
        if (staticObjects.Any(item =>
                item.EntityId <= 0
                || item.EntityId == entity.ShipId
                || item.TypeId <= 0
                || item.OwnerId <= 0
                || item.SolarSystemId != character.SolarSystemId
                || string.IsNullOrWhiteSpace(item.Name)
                || !double.IsFinite(item.PositionX)
                || !double.IsFinite(item.PositionY)
                || !double.IsFinite(item.PositionZ)
                || !double.IsFinite(item.Radius)
                || item.Radius <= 0)
            || staticObjects.Select(item => item.EntityId).Distinct().Count()
                != staticObjects.Count)
        {
            throw new InvalidDataException(
                "The initial solar-system snapshot contains invalid authored objects.");
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
