namespace SpaceSpreadsheetEmulator.Cluster.Directory;

public interface IPartitionDirectory
{
    ValueTask<PartitionAssignment?> ResolveAsync(
        PartitionKey partition,
        CancellationToken cancellationToken = default);
}
