using Grpc.Net.Client;
using Microsoft.Extensions.DependencyInjection;
using SpaceSpreadsheetEmulator.Cluster.Contracts.V1;
using SpaceSpreadsheetEmulator.Cluster.Directory;
using SpaceSpreadsheetEmulator.Coordinator.IntegrationTests.Support;
using SpaceSpreadsheetEmulator.Primitives.Identifiers;

namespace SpaceSpreadsheetEmulator.Coordinator.IntegrationTests.Grpc;

public class CoordinatorHostTests
{
    [Fact]
    public async Task ManagementHealthEndpointsAreHealthy()
    {
        await using var factory = new CoordinatorWebApplicationFactory();
        using HttpClient client = factory.CreateClient();

        Assert.True((await client.GetAsync("/health/live")).IsSuccessStatusCode);
        Assert.True((await client.GetAsync("/health/ready")).IsSuccessStatusCode);
    }

    [Fact]
    public async Task VersionedGrpcDirectoryLookupReturnsAssignment()
    {
        await using var factory = new CoordinatorWebApplicationFactory();
        InMemoryPartitionDirectory directory = factory.Services.GetRequiredService<InMemoryPartitionDirectory>();
        directory.Set(new PartitionAssignment(
            new PartitionKey(PartitionKind.SolarSystem, "30000142"),
            new NodeId("worker-a"),
            new SimulationEpoch(7),
            new Uri("http://127.0.0.1:7000")));
        using GrpcChannel channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
        {
            HttpHandler = factory.Server.CreateHandler(),
        });
        var client = new ClusterDirectory.ClusterDirectoryClient(channel);

        ResolvePartitionResponse response = await client.ResolvePartitionAsync(new ResolvePartitionRequest
        {
            Kind = (int)PartitionKind.SolarSystem,
            Key = "30000142",
        });

        Assert.True(response.Found);
        Assert.Equal("worker-a", response.OwnerNodeId);
        Assert.Equal(7ul, response.Epoch);
    }

    [Fact]
    public async Task ConfiguredBootstrapSolarSystemsArePublished()
    {
        await using var factory = new CoordinatorWebApplicationFactory();
        using GrpcChannel channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
        {
            HttpHandler = factory.Server.CreateHandler(),
        });
        var client = new ClusterDirectory.ClusterDirectoryClient(channel);

        ResolvePartitionResponse first = await client.ResolvePartitionAsync(new ResolvePartitionRequest
        {
            Kind = (int)PartitionKind.SolarSystem,
            Key = "30002780",
        });
        ResolvePartitionResponse second = await client.ResolvePartitionAsync(new ResolvePartitionRequest
        {
            Kind = (int)PartitionKind.SolarSystem,
            Key = "30000142",
        });

        Assert.True(first.Found);
        Assert.Equal("worker-local", first.OwnerNodeId);
        Assert.Equal(11ul, first.Epoch);
        Assert.Equal("http://127.0.0.1:5199/", first.Endpoint);
        Assert.True(second.Found);
        Assert.Equal(13ul, second.Epoch);
    }
}
