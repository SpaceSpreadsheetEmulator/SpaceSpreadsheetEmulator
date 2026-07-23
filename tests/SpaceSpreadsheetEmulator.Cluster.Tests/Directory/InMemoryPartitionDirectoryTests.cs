using SpaceSpreadsheetEmulator.Cluster.Directory;
using SpaceSpreadsheetEmulator.Primitives.Identifiers;

namespace SpaceSpreadsheetEmulator.Cluster.Tests.Directory;

public class InMemoryPartitionDirectoryTests
{
    [Fact]
    public async Task AssignmentCanBeResolvedAndRemoved()
    {
        var directory = new InMemoryPartitionDirectory();
        var key = new PartitionKey(PartitionKind.SolarSystem, "30000142");
        var assignment = new PartitionAssignment(
            key,
            new NodeId("worker-a"),
            new SimulationEpoch(3),
            new Uri("http://127.0.0.1:7000"));

        directory.Set(assignment);

        Assert.Equal(assignment, await directory.ResolveAsync(key));
        Assert.True(directory.Remove(key));
        Assert.Null(await directory.ResolveAsync(key));
    }

    [Fact]
    public async Task LookupHonorsCancellation()
    {
        var directory = new InMemoryPartitionDirectory();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await directory.ResolveAsync(
                new PartitionKey(PartitionKind.ChatChannel, "local"),
                cancellation.Token));
    }

    [Fact]
    public void PartitionKeyValidatesKindAndLength()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PartitionKey((PartitionKind)99, "x"));
        Assert.Throws<ArgumentException>(() => new PartitionKey(PartitionKind.MarketRegion, ""));
    }
}
