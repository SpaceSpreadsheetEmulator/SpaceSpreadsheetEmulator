using System.Collections.Immutable;
using SpaceSpreadsheetEmulator.Backplane.Contracts.V1;
using SpaceSpreadsheetEmulator.Protocol.MachoNet;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Gateway.Compatibility;

/// <summary>
/// Maps neutral selected-character state to build 3396210 session changes.
/// </summary>
internal static class Build3396210SessionMapper
{
    private const string SessionChangeType =
        "carbon.common.script.net.machoNetPacket.SessionChangeNotification";

    public static MachoPacket CreateCharacterSelection(
        ulong gatewaySessionId,
        long proxyNodeId,
        long clientId,
        long userId,
        CharacterSummary character,
        ImmutableArray<PyValue> requestExtensions)
    {
        ArgumentNullException.ThrowIfNull(character);
        PyValue stationId = character.HasStationId
            ? new PyInteger(character.StationId)
            : PyNull.Instance;
        long locationId = character.HasStationId
            ? character.StationId
            : character.SolarSystemId;
        var changes = new List<(string Key, PyValue Value)>
        {
            ("genderID", Change(new PyBoolean(false))),
            ("bloodlineID", Change(new PyInteger(character.BloodlineId))),
            ("raceID", Change(new PyInteger(character.RaceId))),
            ("corpid", Change(new PyInteger(character.CorporationId))),
            ("regionid", Change(new PyInteger(character.RegionId))),
            ("stationid", Change(stationId)),
            ("locationid", Change(new PyInteger(locationId))),
        };
        if (!character.HasStationId)
        {
            changes.Add(("solarsystemid", Change(new PyInteger(character.SolarSystemId))));
        }

        changes.AddRange(
        [
            ("hqID", Change(new PyInteger(character.HeadquartersStationId))),
            ("solarsystemid2", Change(new PyInteger(character.SolarSystemId))),
            ("shipid", Change(new PyInteger(character.ShipId))),
            ("charid", Change(new PyInteger(character.CharacterId))),
            ("constellationid", Change(new PyInteger(character.ConstellationId))),
        ]);

        return Create(
            gatewaySessionId,
            proxyNodeId,
            clientId,
            userId,
            Dictionary([.. changes]),
            requestExtensions);
    }

    public static MachoPacket CreateUndock(
        ulong gatewaySessionId,
        long proxyNodeId,
        long clientId,
        long userId,
        int previousStationId,
        CharacterSummary character,
        ImmutableArray<PyValue> requestExtensions)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(previousStationId);
        ArgumentNullException.ThrowIfNull(character);
        return CreateLocationChange(
            gatewaySessionId,
            proxyNodeId,
            clientId,
            userId,
            previousStationId,
            stationId: null,
            character.SolarSystemId,
            requestExtensions);
    }

    public static MachoPacket CreateDock(
        ulong gatewaySessionId,
        long proxyNodeId,
        long clientId,
        long userId,
        int stationId,
        CharacterSummary character,
        ImmutableArray<PyValue> requestExtensions)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stationId);
        ArgumentNullException.ThrowIfNull(character);
        return CreateLocationChange(
            gatewaySessionId,
            proxyNodeId,
            clientId,
            userId,
            previousStationId: null,
            stationId,
            character.SolarSystemId,
            requestExtensions);
    }

    private static MachoPacket CreateLocationChange(
        ulong gatewaySessionId,
        long proxyNodeId,
        long clientId,
        long userId,
        int? previousStationId,
        int? stationId,
        int solarSystemId,
        ImmutableArray<PyValue> requestExtensions)
    {
        PyValue previousStation = NumberOrNull(previousStationId);
        PyValue currentStation = NumberOrNull(stationId);
        PyValue previousLocation = previousStationId is int
            ? previousStation
            : new PyInteger(solarSystemId);
        PyValue currentLocation = stationId is int
            ? currentStation
            : new PyInteger(solarSystemId);
        return Create(
            gatewaySessionId,
            proxyNodeId,
            clientId,
            userId,
            Dictionary(
                ("stationid", Change(previousStation, currentStation)),
                ("locationid", Change(previousLocation, currentLocation)),
                ("solarsystemid", Change(
                    previousStationId is int ? PyNull.Instance : new PyInteger(solarSystemId),
                    stationId is int ? PyNull.Instance : new PyInteger(solarSystemId)))),
            requestExtensions);
    }

    private static MachoPacket Create(
        ulong gatewaySessionId,
        long proxyNodeId,
        long clientId,
        long userId,
        PyDictionary changes,
        ImmutableArray<PyValue> requestExtensions)
        => new(
            SessionChangeType,
            16,
            new MachoNodeAddress(proxyNodeId, null),
            new MachoClientAddress(clientId, null),
            userId,
            new PyTuple(
                new PyInteger(checked((long)gatewaySessionId)),
                new PyTuple(new PyInteger(0), changes),
                new PyList()),
            CreateExtensions(requestExtensions));

    private static PyTuple Change(PyValue current)
        => new(PyNull.Instance, current);

    private static PyTuple Change(PyValue previous, PyValue current)
        => new(previous, current);

    private static PyValue NumberOrNull(int? value)
        => value is int number ? new PyInteger(number) : PyNull.Instance;

    private static PyDictionary Dictionary(params (string Key, PyValue Value)[] entries)
        => new(entries.Select(
            entry => new PyDictionaryEntry(new PyText(entry.Key), entry.Value)).ToArray());

    private static ImmutableArray<PyValue> CreateExtensions(ImmutableArray<PyValue> request)
        =>
        [
            request.ElementAtOrDefault(0) ?? PyNull.Instance,
            request.ElementAtOrDefault(1) ?? PyNull.Instance,
            request.ElementAtOrDefault(2) ?? PyNull.Instance,
            request.ElementAtOrDefault(3) ?? PyNull.Instance,
            request.ElementAtOrDefault(4) ?? PyNull.Instance,
            new PyBoolean(false),
            new PyInteger(0),
            new PyInteger(1000),
            PyNull.Instance,
        ];
}
