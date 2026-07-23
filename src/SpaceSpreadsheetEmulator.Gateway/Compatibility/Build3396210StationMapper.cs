using SpaceSpreadsheetEmulator.Backplane.Contracts.V1;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Gateway.Compatibility;

/// <summary>
/// Maps protocol-neutral station records to the build-3396210 station-info rowset.
/// </summary>
internal static class Build3396210StationMapper
{
    private const int MaximumStations = 10_000;
    private static readonly Build3396210RowField[] Fields =
    [
        new("stationID", 3),
        new("solarSystemID", 3),
        new("operationID", 17),
        new("stationTypeID", 3),
        new("ownerID", 3),
    ];

    public static PyValue? CreateStationInfo(IReadOnlyList<StationSummary> stations)
    {
        ArgumentNullException.ThrowIfNull(stations);
        if (stations.Count is 0 or > MaximumStations)
        {
            return null;
        }

        var stationIds = new HashSet<int>();
        IReadOnlyList<PyValue>[] rows = new IReadOnlyList<PyValue>[stations.Count];
        for (int index = 0; index < stations.Count; index++)
        {
            StationSummary station = stations[index];
            if (station.StationId <= 0
                || !stationIds.Add(station.StationId)
                || station.SolarSystemId <= 0
                || station.OperationId is <= 0 or > byte.MaxValue
                || station.StationTypeId <= 0
                || station.OwnerId <= 0)
            {
                return null;
            }

            rows[index] =
            [
                new PyInteger(station.StationId),
                new PyInteger(station.SolarSystemId),
                new PyInteger(station.OperationId),
                new PyInteger(station.StationTypeId),
                new PyInteger(station.OwnerId),
            ];
        }

        return Build3396210PackedRowBuilder.CreateRowset(Fields, rows);
    }
}
