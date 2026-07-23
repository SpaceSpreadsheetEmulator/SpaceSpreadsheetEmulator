namespace SpaceSpreadsheetEmulator.CaptureInspector.Models;

public static class IdentifierFields
{
    public static readonly IReadOnlyList<string> Supported =
    [
        "typeID", "stationID", "solarSystemID", "constellationID", "regionID",
        "corporationID", "factionID", "raceID", "bloodlineID", "ancestryID",
    ];

    public static Dictionary<string, bool> DefaultSettings() => Supported.ToDictionary(static field => field, static _ => true, StringComparer.Ordinal);
}
