using System.IO.Abstractions;
using SpaceSpreadsheetEmulator.Worker.IntegrationTests.Support;

namespace SpaceSpreadsheetEmulator.Worker.IntegrationTests.Health;

public class WorkerHealthTests
{
    [Fact]
    public async Task LiveAndReadyEndpointsAreHealthy()
    {
        await using WorkerWebApplicationFactory factory = WorkerWebApplicationFactory.UnitTest(
            new FileSystem());
        using HttpClient client = factory.CreateClient();

        Assert.True((await client.GetAsync("/health/live")).IsSuccessStatusCode);
        Assert.True((await client.GetAsync("/health/ready")).IsSuccessStatusCode);
    }

    [Fact]
    public async Task LoginEnabledWorkerRequiresGameDatabaseConfiguration()
    {
        await using TestStaticDataArtifact artifact = await TestStaticDataArtifact.CreateAsync(
            new FileSystem());
        await using WorkerWebApplicationFactory factory =
            WorkerWebApplicationFactory.IntegrationTest(
                new FileSystem(),
                artifact.ArtifactDirectory);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => factory.CreateClient());

        Assert.Contains("ConnectionStrings:GameDatabase", error.Message, StringComparison.Ordinal);
    }
}
