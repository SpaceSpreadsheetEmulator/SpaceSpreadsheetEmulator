namespace SpaceSpreadsheetEmulator.Cluster.Directory;

/// <summary>
/// Resolves logical gameplay partitions to their current owning cluster nodes.
/// </summary>
public interface IPartitionDirectory
{
    ValueTask<PartitionAssignment?> ResolveAsync(
        PartitionKey partition,
        CancellationToken cancellationToken = default);
}
