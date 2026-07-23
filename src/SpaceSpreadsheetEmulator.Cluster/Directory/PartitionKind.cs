namespace SpaceSpreadsheetEmulator.Cluster.Directory;

/// <summary>
/// Identifies the ownership and routing model used by a logical cluster partition.
/// </summary>
public enum PartitionKind
{
    SolarSystem = 1,
    MarketRegion = 2,
    ChatChannel = 3,
}
