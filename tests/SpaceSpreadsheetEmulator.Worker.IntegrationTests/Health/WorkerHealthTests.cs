using Microsoft.AspNetCore.Mvc.Testing;

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
}
