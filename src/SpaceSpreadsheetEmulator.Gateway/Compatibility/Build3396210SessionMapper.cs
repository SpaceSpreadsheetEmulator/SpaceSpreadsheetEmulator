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

        return new MachoPacket(
            "carbon.common.script.net.machoNetPacket.SessionChangeNotification",
            16,
            new MachoNodeAddress(proxyNodeId, null),
            new MachoClientAddress(clientId, null),
            userId,
            new PyTuple(
                new PyInteger(checked((long)gatewaySessionId)),
                new PyTuple(
                    new PyInteger(0),
                    Dictionary(
                        ("genderID", Change(new PyBoolean(false))),
                        ("bloodlineID", Change(new PyInteger(character.BloodlineId))),
                        ("raceID", Change(new PyInteger(character.RaceId))),
                        ("corpid", Change(new PyInteger(character.CorporationId))),
                        ("regionid", Change(new PyInteger(character.RegionId))),
                        ("stationid", Change(stationId)),
                        ("locationid", Change(new PyInteger(locationId))),
                        ("hqID", Change(new PyInteger(character.HeadquartersStationId))),
                        ("solarsystemid2", Change(new PyInteger(character.SolarSystemId))),
                        ("shipid", Change(new PyInteger(character.ShipId))),
                        ("charid", Change(new PyInteger(character.CharacterId))),
                        ("constellationid", Change(new PyInteger(character.ConstellationId))))),
                new PyList()),
            CreateExtensions(requestExtensions));
    }

    private static PyTuple Change(PyValue current)
        => new(PyNull.Instance, current);

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
