using SpaceSpreadsheetEmulator.Primitives.Identifiers;

namespace SpaceSpreadsheetEmulator.Cluster.Directory;

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
