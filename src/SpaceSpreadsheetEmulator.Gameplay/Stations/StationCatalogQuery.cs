using SpaceSpreadsheetEmulator.StaticData;

namespace SpaceSpreadsheetEmulator.Gameplay.Stations;

/// <summary>
/// Describes the protocol-neutral station fields exposed by the pinned static-data build.
/// </summary>
public sealed record StationCatalogEntry(
    int StationId,
    int SolarSystemId,
    int OperationId,
    int StationTypeId,
    int OwnerId);

/// <summary>
/// Loads and validates the NPC station catalog used by station-facing gameplay queries.
/// </summary>
public interface IStationCatalogQuery
{
    ValueTask<IReadOnlyList<StationCatalogEntry>> ExecuteAsync(
        CancellationToken cancellationToken = default);
}

public sealed class StationCatalogQuery(IStaticDataStore staticData) : IStationCatalogQuery
{
    public async ValueTask<IReadOnlyList<StationCatalogEntry>> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<StaticDataRecord> records = await staticData.ListAsync(
            StaticDataEntityKind.NpcStation,
            cancellationToken);
        return records.Select(Map).ToArray();
    }

    private static StationCatalogEntry Map(StaticDataRecord station)
        => new(
            checked((int)station.Id),
            Required(station, station.ParentId, "solar-system"),
            Required(station, station.OperationId, "operation"),
            Required(station, station.TypeId, "type"),
            Required(station, station.OwnerId, "owner"));

    private static int Required(StaticDataRecord station, long? value, string relationship)
        => checked((int)(value
            ?? throw new InvalidDataException(
                $"Required station {relationship} relationship for station/{station.Id} is missing.")));
}
