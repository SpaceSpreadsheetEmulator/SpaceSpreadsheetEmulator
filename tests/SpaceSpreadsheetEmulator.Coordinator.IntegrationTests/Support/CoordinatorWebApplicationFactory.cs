using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SpaceSpreadsheetEmulator.Coordinator.IntegrationTests.Support;

internal sealed class CoordinatorWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("UnitTest");
    }
}
