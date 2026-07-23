using System.Collections.Concurrent;

namespace SpaceSpreadsheetEmulator.Cluster.Directory;

public sealed class InMemoryPartitionDirectory : IPartitionDirectory
{
    private readonly ConcurrentDictionary<PartitionKey, PartitionAssignment> assignments = new();

    public ValueTask<PartitionAssignment?> ResolveAsync(
        PartitionKey partition,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        assignments.TryGetValue(partition, out PartitionAssignment? assignment);
        return ValueTask.FromResult(assignment);
    }

    public void Set(PartitionAssignment assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        assignments[assignment.Partition] = assignment.Validate();
    }

    public bool Remove(PartitionKey partition) => assignments.TryRemove(partition, out _);
}
