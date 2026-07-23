using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using SpaceSpreadsheetEmulator.Worker.IntegrationTests.Support;

namespace SpaceSpreadsheetEmulator.Worker.IntegrationTests.Health;

public class WorkerHealthTests
{
    [Fact]
    public async Task LiveAndReadyEndpointsAreHealthy()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using HttpClient client = factory.CreateClient();

        Assert.True((await client.GetAsync("/health/live")).IsSuccessStatusCode);
        Assert.True((await client.GetAsync("/health/ready")).IsSuccessStatusCode);
    }

    [Fact]
    public async Task LoginEnabledWorkerRequiresGameDatabaseConfiguration()
    {
        await using TestStaticDataArtifact artifact = await TestStaticDataArtifact.CreateAsync();
        await using WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder
                .UseSetting("Worker:Login:Enabled", "true")
                .UseSetting("Worker:Login:ArtifactDirectory", artifact.ArtifactDirectory)
                .UseSetting("Worker:Login:DevelopmentEnrollmentEnabled", "true"));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => factory.CreateClient());

        Assert.Contains("ConnectionStrings:GameDatabase", error.Message, StringComparison.Ordinal);
    }
}
