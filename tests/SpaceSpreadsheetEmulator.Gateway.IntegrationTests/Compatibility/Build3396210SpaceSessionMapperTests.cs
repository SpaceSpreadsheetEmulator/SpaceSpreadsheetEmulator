using System.Buffers;
using System.Buffers.Binary;
using SpaceSpreadsheetEmulator.Backplane.Contracts.V1;
using SpaceSpreadsheetEmulator.Gateway.Backplane;
using SpaceSpreadsheetEmulator.Gateway.Compatibility;
using SpaceSpreadsheetEmulator.Gateway.IntegrationTests.Support;
using SpaceSpreadsheetEmulator.Protocol;
using SpaceSpreadsheetEmulator.Protocol.Codec;
using SpaceSpreadsheetEmulator.Protocol.MachoNet;
using SpaceSpreadsheetEmulator.Protocol.Profiles;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Gateway.IntegrationTests.Compatibility;

public sealed class Build3396210SpaceSessionMapperTests
{
    [Fact]
    public void SnapshotMapsSelectedShipToSetState()
    {
        CharacterSummary character = Character();
        SolarSystemEntityState entity = Entity(tick: 42);

        MachoPacket packet = Build3396210SpaceSessionMapper.CreateSnapshot(
            1,
            7,
            character,
            entity,
            StaticObjects(),
            new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal("DoDestinyUpdate", Broadcast(packet).Scope);
        PyTuple arguments = Arguments(packet);
        PyList updates = Assert.IsType<PyList>(arguments.Items[0]);
        PyTuple update = Assert.IsType<PyTuple>(Assert.Single(updates.Items));
        Assert.Equal(42, Assert.IsType<PyInteger>(update.Items[0]).Value);
        PyTuple action = Assert.IsType<PyTuple>(update.Items[1]);
        Assert.Equal("SetState", Text(action.Items[0]));
        PyTuple actionArguments = Assert.IsType<PyTuple>(action.Items[1]);
        PyObject state = Assert.IsType<PyObject>(Assert.Single(actionArguments.Items));
        Assert.Equal("utillib.KeyVal", Text(state.Type));
        PyDictionary fields = Assert.IsType<PyDictionary>(state.State);
        Assert.Equal(character.ShipId, Assert.IsType<PyInteger>(Value(fields, "ego")).Value);
        PyBuffer balls = Assert.IsType<PyBuffer>(Value(fields, "state"));
        Assert.Equal(161, balls.Value.Length);
        ReadOnlySpan<byte> data = balls.Value.AsSpan();
        Assert.Equal(0, data[0]);
        Assert.Equal(42u, BinaryPrimitives.ReadUInt32LittleEndian(data[1..]));
        Assert.Equal(character.ShipId, BinaryPrimitives.ReadInt64LittleEndian(data[5..]));
        Assert.Equal(2, data[13]);
        Assert.Equal(39, BitConverter.Int32BitsToSingle(
            BinaryPrimitives.ReadInt32LittleEndian(data[14..])));
        Assert.Equal(entity.PositionX, BitConverter.Int64BitsToDouble(
            BinaryPrimitives.ReadInt64LittleEndian(data[18..])));
        Assert.Equal(0, data[42]);
        Assert.Equal(byte.MaxValue, data[43]);
        AssertStaticBall(data[44..], 40_176_368, 2_150_000);
        AssertStaticBall(data[83..], 50_006_751, 3_532);
        AssertStaticBall(data[122..], 60_000_004, 10_000);

        PyList slims = Assert.IsType<PyList>(Value(fields, "slims"));
        Assert.Equal(4, slims.Items.Length);
        PyObject slim = Assert.IsType<PyObject>(slims.Items[0]);
        Assert.Equal(
            "eve.common.script.util.slimItem.SlimItem",
            Text(slim.Type));
        PyDictionary slimFields = Assert.IsType<PyDictionary>(slim.State);
        Assert.Equal(601, Assert.IsType<PyInteger>(Value(slimFields, "typeID")).Value);
        Assert.Equal(25, Assert.IsType<PyInteger>(Value(slimFields, "groupID")).Value);
        Assert.Equal(6, Assert.IsType<PyInteger>(Value(slimFields, "categoryID")).Value);
        AssertStaticSlim(slims.Items[1], 40_176_368, 7, 2, "Test Planet", 1);
        AssertStaticSlim(slims.Items[2], 50_006_751, 10, 2, "Test Gate", 500_001);
        AssertStaticSlim(
            slims.Items[3],
            60_000_004,
            15,
            3,
            "Test Station",
            1_000_002);

        PyDictionary damage = Assert.IsType<PyDictionary>(Value(fields, "damageState"));
        PyList damageState = Assert.IsType<PyList>(Assert.Single(damage.Entries).Value);
        Assert.Equal(3, damageState.Items.Length);
        PyTuple shield = Assert.IsType<PyTuple>(damageState.Items[0]);
        Assert.Equal(1, Assert.IsType<PyFloat>(shield.Items[0]).Value);
        Assert.Equal(0, Assert.IsType<PyFloat>(shield.Items[1]).Value);
        Assert.IsType<PyBigInteger>(shield.Items[2]);
        Assert.Equal(1, Assert.IsType<PyFloat>(damageState.Items[1]).Value);
        Assert.Equal(1, Assert.IsType<PyFloat>(damageState.Items[2]).Value);

        IReadOnlyDictionary<string, PyValue> solarSystem = PackedRowTestReader.Read(
            Assert.IsType<PyPackedRow>(Value(fields, "solItem")));
        Assert.Equal(character.SolarSystemId, Assert.IsType<PyInteger>(
            solarSystem["itemID"]).Value);
        Assert.Equal(5, Assert.IsType<PyInteger>(solarSystem["typeID"]).Value);
        Assert.Equal(character.ConstellationId, Assert.IsType<PyInteger>(
            solarSystem["locationID"]).Value);
        Assert.Equal(5, Assert.IsType<PyInteger>(solarSystem["groupID"]).Value);
        Assert.Equal(2, Assert.IsType<PyInteger>(solarSystem["categoryID"]).Value);
    }

    [Fact]
    public void MovementMapsToOneOrderedDestinyBatch()
    {
        MachoPacket packet = Build3396210SpaceSessionMapper.CreateMovement(
            1,
            7,
            Entity(tick: 43));

        PyList updates = Assert.IsType<PyList>(Arguments(packet).Items[0]);
        Assert.Collection(
            updates.Items,
            update => Assert.Equal(
                "SetBallPosition",
                Text(Assert.IsType<PyTuple>(
                    Assert.IsType<PyTuple>(update).Items[1]).Items[0])),
            update => Assert.Equal(
                "SetBallVelocity",
                Text(Assert.IsType<PyTuple>(
                    Assert.IsType<PyTuple>(update).Items[1]).Items[0])));
    }

    [Fact]
    public void DockLifecycleUsesObservedScopesAndAudienceKeys()
    {
        MachoPacket accepted =
            Build3396210SpaceSessionMapper.CreateDockingAccepted(1, 7, 60_000_004);
        MachoPacket disconnected =
            Build3396210SpaceSessionMapper.CreateObjectDisconnect(
                1,
                7,
                "N=1:80200",
                1_000_007);
        MachoPacket finished =
            Build3396210SpaceSessionMapper.CreateDockingFinished(1, 7, 60_000_004);

        Assert.Equal("clientID", Broadcast(accepted).Service);
        Assert.Equal("+clientID", Broadcast(disconnected).Service);
        Assert.Equal("charid", Broadcast(finished).Service);
        Assert.Equal(60_000_004, Assert.IsType<PyInteger>(
            Assert.Single(Arguments(accepted).Items)).Value);
        Assert.Collection(
            Arguments(disconnected).Items,
            value => Assert.Equal("N=1:80200", Text(value)),
            value => Assert.Equal(1_000_007, Assert.IsType<PyInteger>(value).Value),
            value => Assert.IsType<PyNull>(value));
    }

    private static CharacterSummary Character()
        => new()
        {
            CharacterId = 90_000_007,
            ShipId = 140_000_007,
            ShipTypeId = 601,
            ShipName = "Cell Reference",
            CorporationId = 1_000_002,
            SolarSystemId = 30_000_142,
            ConstellationId = 20_000_020,
        };

    private static SolarSystemStaticObjectState[] StaticObjects()
        =>
        [
            new(
                60_000_004,
                1531,
                "Test Station",
                SolarSystemStaticObjectKind.Station,
                30_000_142,
                0,
                0,
                0,
                10_000,
                null,
                1_000_002),
            new(
                40_176_368,
                2016,
                "Test Planet",
                SolarSystemStaticObjectKind.Planet,
                30_000_142,
                1_000_000,
                0,
                0,
                2_150_000,
                null,
                1),
            new(
                50_006_751,
                16,
                "Test Gate",
                SolarSystemStaticObjectKind.JumpGate,
                30_000_142,
                0,
                1_000_000,
                0,
                3_532,
                30_002_780,
                500_001),
        ];

    private static SolarSystemEntityState Entity(ulong tick)
        => new(
            90_000_007,
            140_000_007,
            30_000_142,
            7,
            tick,
            100,
            -50,
            25,
            10,
            0,
            0);

    private static MachoBroadcastAddress Broadcast(MachoPacket packet)
        => Assert.IsType<MachoBroadcastAddress>(packet.Destination);

    private static PyTuple Arguments(MachoPacket packet)
    {
        PyTuple payload = Assert.IsType<PyTuple>(packet.Payload);
        PyTuple envelope = Assert.IsType<PyTuple>(Assert.Single(payload.Items));
        PySubstream substream = Assert.IsType<PySubstream>(envelope.Items[1]);
        DecodeResult<PyValue> decoded = BlueMarshalCodec.Decode(
            new ReadOnlySequence<byte>(substream.Data.AsMemory()),
            ProtocolProfileCatalog.GetRequired(3_396_210));
        Assert.True(decoded.IsSuccess, decoded.Error?.ToString());
        PyTuple body = Assert.IsType<PyTuple>(decoded.Value);
        return Assert.IsType<PyTuple>(Assert.IsType<PyTuple>(body.Items[1]).Items[1]);
    }

    private static PyValue Value(PyDictionary dictionary, string key)
        => dictionary.Entries.Single(
            entry => string.Equals(Text(entry.Key), key, StringComparison.Ordinal)).Value;

    private static void AssertStaticBall(
        ReadOnlySpan<byte> data,
        long entityId,
        float radius)
    {
        Assert.Equal(entityId, BinaryPrimitives.ReadInt64LittleEndian(data));
        Assert.Equal(11, data[8]);
        Assert.Equal(radius, BitConverter.Int32BitsToSingle(
            BinaryPrimitives.ReadInt32LittleEndian(data[9..])));
        Assert.Equal(6, data[37]);
        Assert.Equal(byte.MaxValue, data[38]);
    }

    private static void AssertStaticSlim(
        PyValue value,
        long entityId,
        int groupId,
        int categoryId,
        string name,
        int ownerId)
    {
        PyObject slim = Assert.IsType<PyObject>(value);
        PyDictionary fields = Assert.IsType<PyDictionary>(slim.State);
        Assert.Equal(entityId, Assert.IsType<PyInteger>(Value(fields, "itemID")).Value);
        Assert.Equal(groupId, Assert.IsType<PyInteger>(Value(fields, "groupID")).Value);
        Assert.Equal(categoryId, Assert.IsType<PyInteger>(
            Value(fields, "categoryID")).Value);
        Assert.Equal(name, Text(Value(fields, "name")));
        Assert.Equal(ownerId, Assert.IsType<PyInteger>(Value(fields, "ownerID")).Value);
    }

    private static string Text(PyValue value)
        => value switch
        {
            PyText text => text.Value,
            PyBuffer buffer => System.Text.Encoding.UTF8.GetString(buffer.Value.AsSpan()),
            _ => throw new Xunit.Sdk.XunitException(
                $"Expected text-compatible value, got {value.GetType().Name}."),
        };
}
