using System.Net.Sockets;
using SpaceSpreadsheetEmulator.Gateway.IntegrationTests.Support;

namespace SpaceSpreadsheetEmulator.Gateway.IntegrationTests.Connections;

public class GatewayShutdownTests
{
    [Fact]
    public async Task ShutdownCompletesWithIdleClientsConnected()
    {
        await using GatewayHostHarness gateway = await GatewayHostHarness.StartAsync(2);
        using TcpClient first = await LoopbackClient.ConnectAsync(gateway.Endpoint);
        using TcpClient second = await LoopbackClient.ConnectAsync(gateway.Endpoint);
        await GatewayHostHarness.WaitUntilAsync(() => gateway.Metrics.ActiveConnections == 2);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5), TimeProvider.System);
        await gateway.StopAsync(timeout.Token);

        Assert.Equal(0, gateway.Metrics.ActiveConnections);
    }
}
