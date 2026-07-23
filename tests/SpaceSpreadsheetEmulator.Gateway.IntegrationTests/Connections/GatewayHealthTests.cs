using Microsoft.AspNetCore.Mvc.Testing;

namespace SpaceSpreadsheetEmulator.Gateway.IntegrationTests.Connections;

public class GatewayHealthTests
{
    [Fact]
    public async Task DisabledTcpListenerDoesNotPreventReadiness()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using HttpClient client = factory.CreateClient();

        Assert.True((await client.GetAsync("/health/live")).IsSuccessStatusCode);
        Assert.True((await client.GetAsync("/health/ready")).IsSuccessStatusCode);
    }
}
