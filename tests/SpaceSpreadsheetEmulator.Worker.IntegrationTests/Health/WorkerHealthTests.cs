using SpaceSpreadsheetEmulator.Worker.IntegrationTests.Support;

namespace SpaceSpreadsheetEmulator.Worker.IntegrationTests.Health;

public class WorkerHealthTests
{
    [Fact]
    public async Task LiveAndReadyEndpointsAreHealthy()
    {
        await using WorkerWebApplicationFactory factory = WorkerWebApplicationFactory.UnitTest();
        using HttpClient client = factory.CreateClient();

        Assert.True((await client.GetAsync("/health/live")).IsSuccessStatusCode);
        Assert.True((await client.GetAsync("/health/ready")).IsSuccessStatusCode);
    }

    [Fact]
    public async Task LoginEnabledWorkerRequiresGameDatabaseConfiguration()
    {
        await using TestStaticDataArtifact artifact = await TestStaticDataArtifact.CreateAsync();
        await using WorkerWebApplicationFactory factory =
            WorkerWebApplicationFactory.IntegrationTest(artifact.ArtifactDirectory);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => factory.CreateClient());

        Assert.Contains("ConnectionStrings:GameDatabase", error.Message, StringComparison.Ordinal);
    }
}
