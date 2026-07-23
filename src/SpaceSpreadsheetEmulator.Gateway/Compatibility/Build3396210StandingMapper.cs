using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Gateway.Compatibility;

/// <summary>
/// Builds the NPC-to-NPC standing rowset consumed during build-3396210 session changes.
/// </summary>
internal static class Build3396210StandingMapper
{
    private static readonly Build3396210RowField[] Fields =
    [
        new("fromID", 3),
        new("toID", 3),
        new("standing", 5),
    ];

    public static PyExtendedObject CreateEmptyNpcStandings()
        => Build3396210PackedRowBuilder.CreateRowset(Fields);

    public static PyExtendedObject CreateEmptyCharacterStandings()
        => Build3396210PackedRowBuilder.CreateRowset(
        [
            new Build3396210RowField("fromID", 3),
            new Build3396210RowField("standing", 5),
        ]);
}
