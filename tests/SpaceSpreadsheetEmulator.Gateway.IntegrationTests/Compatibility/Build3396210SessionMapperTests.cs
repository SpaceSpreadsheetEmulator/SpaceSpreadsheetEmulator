using System.Collections.Immutable;
using SpaceSpreadsheetEmulator.Backplane.Contracts.V1;
using SpaceSpreadsheetEmulator.Gateway.Compatibility;
using SpaceSpreadsheetEmulator.Protocol.MachoNet;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Gateway.IntegrationTests.Compatibility;

public sealed class Build3396210SessionMapperTests
{
    private static readonly string[] ExpectedKeys =
    [
        "genderID",
        "bloodlineID",
        "raceID",
        "corpid",
        "regionid",
        "stationid",
        "locationid",
        "hqID",
        "solarsystemid2",
        "shipid",
        "charid",
        "constellationid",
    ];

    [Fact]
    public void DockedSelectionCreatesExactObservedSessionChange()
    {
        CharacterSummary character = Character();
        ImmutableArray<PyValue> requestExtensions =
        [
            new PyText("extension-0"),
            new PyInteger(1),
            new PyBuffer(ImmutableArray.Create<byte>(0x02)),
            new PyText("extension-3"),
            new PyInteger(4),
            new PyText("not-preserved"),
        ];

        MachoPacket packet = Build3396210SessionMapper.CreateCharacterSelection(
            gatewaySessionId: 99,
            proxyNodeId: 1,
            clientId: 1_000_007,
            userId: 7,
            character,
            requestExtensions);

        Assert.Equal("carbon.common.script.net.machoNetPacket.SessionChangeNotification", packet.ObjectTypeName);
        Assert.Equal(16, packet.NumericType);
        Assert.Equal(new MachoNodeAddress(1, null), packet.Source);
        Assert.Equal(new MachoClientAddress(1_000_007, null), packet.Destination);
        Assert.Equal(7, packet.UserId);

        PyTuple payload = Assert.IsType<PyTuple>(packet.Payload);
        Assert.Equal(99, Assert.IsType<PyInteger>(payload.Items[0]).Value);
        PyTuple changeEnvelope = Assert.IsType<PyTuple>(payload.Items[1]);
        Assert.Equal(0, Assert.IsType<PyInteger>(changeEnvelope.Items[0]).Value);
        PyDictionary changes = Assert.IsType<PyDictionary>(changeEnvelope.Items[1]);
        Assert.Equal(ExpectedKeys, changes.Entries.Select(Key).ToArray());
        Assert.All(changes.Entries, entry => Assert.IsType<PyNull>(
            Assert.IsType<PyTuple>(entry.Value).Items[0]));
        Assert.False(Assert.IsType<PyBoolean>(Current(changes, "genderID")).Value);
        Assert.Equal(60_000_004, Integer(changes, "stationid"));
        Assert.Equal(60_000_004, Integer(changes, "locationid"));
        Assert.Equal(60_000_004, Integer(changes, "hqID"));
        Assert.Empty(Assert.IsType<PyList>(payload.Items[2]).Items);

        for (int index = 0; index < 5; index++)
        {
            Assert.Same(requestExtensions[index], packet.Extensions[index]);
        }
        Assert.False(Assert.IsType<PyBoolean>(packet.Extensions[5]).Value);
        Assert.Equal(0, Assert.IsType<PyInteger>(packet.Extensions[6]).Value);
        Assert.Equal(1000, Assert.IsType<PyInteger>(packet.Extensions[7]).Value);
        Assert.IsType<PyNull>(packet.Extensions[8]);
    }

    [Fact]
    public void StationlessSelectionUsesSolarSystemAsLocation()
    {
        CharacterSummary character = Character();
        character.ClearStationId();

        MachoPacket packet = Build3396210SessionMapper.CreateCharacterSelection(
            gatewaySessionId: 100,
            proxyNodeId: 1,
            clientId: 1_000_007,
            userId: 7,
            character,
            []);

        PyDictionary changes = Changes(packet);
        Assert.IsType<PyNull>(Current(changes, "stationid"));
        Assert.Equal(30_002_780, Integer(changes, "locationid"));
        Assert.Equal(30_002_780, Integer(changes, "solarsystemid2"));
    }

    [Fact]
    public void UndockMovesLocationIntoSolarSystem()
    {
        CharacterSummary character = Character();
        character.ClearStationId();

        MachoPacket packet = Build3396210SessionMapper.CreateUndock(
            gatewaySessionId: 100,
            proxyNodeId: 1,
            clientId: 1_000_007,
            userId: 7,
            previousStationId: 60_000_004,
            character,
            []);

        Assert.Equal(16, packet.NumericType);
        PyDictionary changes = Changes(packet);
        Assert.Equal(["stationid", "locationid", "solarsystemid"], changes.Entries.Select(Key));
        Assert.Equal(60_000_004, Integer(changes, "stationid", previous: true));
        Assert.IsType<PyNull>(Current(changes, "stationid"));
        Assert.Equal(60_000_004, Integer(changes, "locationid", previous: true));
        Assert.Equal(30_002_780, Integer(changes, "locationid"));
        Assert.IsType<PyNull>(Previous(changes, "solarsystemid"));
        Assert.Equal(30_002_780, Integer(changes, "solarsystemid"));
    }

    [Fact]
    public void DockMovesLocationIntoStation()
    {
        CharacterSummary character = Character();

        MachoPacket packet = Build3396210SessionMapper.CreateDock(
            gatewaySessionId: 100,
            proxyNodeId: 1,
            clientId: 1_000_007,
            userId: 7,
            stationId: 60_000_004,
            character,
            []);

        PyDictionary changes = Changes(packet);
        Assert.IsType<PyNull>(Previous(changes, "stationid"));
        Assert.Equal(60_000_004, Integer(changes, "stationid"));
        Assert.Equal(30_002_780, Integer(changes, "locationid", previous: true));
        Assert.Equal(60_000_004, Integer(changes, "locationid"));
        Assert.Equal(30_002_780, Integer(changes, "solarsystemid", previous: true));
        Assert.IsType<PyNull>(Current(changes, "solarsystemid"));
    }

    private static CharacterSummary Character()
        => new()
        {
            CharacterId = 90_000_007,
            RaceId = 1,
            BloodlineId = 1,
            CorporationId = 1_000_002,
            HeadquartersStationId = 60_000_004,
            RegionId = 10_000_033,
            StationId = 60_000_004,
            SolarSystemId = 30_002_780,
            ShipId = 190_000_007,
            ConstellationId = 20_000_407,
        };

    private static PyDictionary Changes(MachoPacket packet)
    {
        PyTuple payload = Assert.IsType<PyTuple>(packet.Payload);
        PyTuple envelope = Assert.IsType<PyTuple>(payload.Items[1]);
        return Assert.IsType<PyDictionary>(envelope.Items[1]);
    }

    private static long Integer(PyDictionary changes, string key)
        => Assert.IsType<PyInteger>(Current(changes, key)).Value;

    private static long Integer(PyDictionary changes, string key, bool previous)
        => Assert.IsType<PyInteger>(
            previous ? Previous(changes, key) : Current(changes, key)).Value;

    private static PyValue Current(PyDictionary changes, string key)
        => Assert.IsType<PyTuple>(changes.Entries.Single(entry => Key(entry) == key).Value).Items[1];

    private static PyValue Previous(PyDictionary changes, string key)
        => Assert.IsType<PyTuple>(changes.Entries.Single(entry => Key(entry) == key).Value).Items[0];

    private static string Key(PyDictionaryEntry entry)
        => Assert.IsType<PyText>(entry.Key).Value;
}
