using SpaceSpreadsheetEmulator.Primitives.Identifiers;

namespace SpaceSpreadsheetEmulator.Cluster.Tests.Primitives;

public class IdentifierTests
{
    [Fact]
    public void SolarSystemIdMustBePositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SolarSystemId(0));
        Assert.Equal(30_000_142, new SolarSystemId(30_000_142).Value);
    }

    [Fact]
    public void NodeIdMustBeNonEmptyAndBounded()
    {
        Assert.Throws<ArgumentException>(() => new NodeId(" "));
        Assert.Throws<ArgumentException>(() => new NodeId(new string('x', NodeId.MaximumLength + 1)));
        Assert.Equal("worker-a", new NodeId("worker-a").Value);
    }

    [Fact]
    public void EpochMustBePositiveAndAdvancesMonotonically()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SimulationEpoch(0));
        Assert.Equal(2ul, new SimulationEpoch(1).Next().Value);
    }
}
