using SpaceSpreadsheetEmulator.Primitives.Identifiers;

namespace SpaceSpreadsheetEmulator.Cluster.Directory;

/// <summary>
/// Describes the node, ownership epoch, and endpoint currently assigned to a logical partition.
/// </summary>
public sealed record PartitionAssignment(
    PartitionKey Partition,
    NodeId OwnerNodeId,
    SimulationEpoch Epoch,
    Uri Endpoint)
{
    public PartitionAssignment Validate()
    {
        if (!Endpoint.IsAbsoluteUri)
        {
            throw new ArgumentException("A partition endpoint must be absolute.", nameof(Endpoint));
        }

        return this;
    }
}
