using System.Net.Sockets;
using SpaceSpreadsheetEmulator.Gateway.Connections;
using SpaceSpreadsheetEmulator.Gateway.IntegrationTests.Support;

namespace SpaceSpreadsheetEmulator.Gateway.IntegrationTests.Connections;

public class ConnectionLimitTests
{
    [Fact]
    public async Task FullGatewayRejectsThenImmediatelyReusesReleasedSlot()
    {
        await using GatewayHostHarness gateway = await GatewayHostHarness.StartAsync(2);
        using TcpClient first = await LoopbackClient.ConnectAsync(gateway.Endpoint);
        using TcpClient second = await LoopbackClient.ConnectAsync(gateway.Endpoint);
        await GatewayHostHarness.WaitUntilAsync(() => gateway.Metrics.ActiveConnections == 2);

        using TcpClient rejected = await LoopbackClient.ConnectAsync(gateway.Endpoint);
        await GatewayHostHarness.WaitUntilAsync(() => gateway.Metrics.RejectedConnections == 1);
        Assert.Equal(0, await LoopbackClient.ReadUntilClosedAsync(rejected.GetStream()));
        Assert.Equal(2, gateway.Metrics.ActiveConnections);

        first.Dispose();
        await GatewayHostHarness.WaitUntilAsync(() => gateway.Metrics.ActiveConnections == 1);
        using TcpClient replacement = await LoopbackClient.ConnectAsync(gateway.Endpoint);
        await GatewayHostHarness.WaitUntilAsync(() => gateway.Metrics.ActiveConnections == 2);
        using var protocol = new ProtocolLoopbackClient(replacement);
        Assert.IsType<SpaceSpreadsheetEmulator.Protocol.Values.PyTuple>(await protocol.ReadValueAsync());
    }

    [Fact]
    public void DefaultIsOneDocumentedProjectWideConstant()
    {
        Assert.Equal(256, GatewayConnectionDefaults.SoftConnectionLimit);
        Assert.Equal(1_024, 4 * new GatewayConnectionOptions().SoftConnectionLimit);
        Assert.Equal(512, new GatewayConnectionOptions { SoftConnectionLimit = 512 }.SoftConnectionLimit);
    }

    [Fact]
    public async Task OptionalDefaultCapacitySmokeTest()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("SSE_RUN_CONNECTION_SMOKE"), "1", StringComparison.Ordinal))
        {
            return;
        }

        await using GatewayHostHarness gateway = await GatewayHostHarness.StartAsync(
            GatewayConnectionDefaults.SoftConnectionLimit);
        var clients = new List<TcpClient>();
        try
        {
            for (int index = 0; index < GatewayConnectionDefaults.SoftConnectionLimit; index++)
            {
                clients.Add(await LoopbackClient.ConnectAsync(gateway.Endpoint));
            }

            await GatewayHostHarness.WaitUntilAsync(() =>
                gateway.Metrics.ActiveConnections == GatewayConnectionDefaults.SoftConnectionLimit);
        }
        finally
        {
            clients.ForEach(client => client.Dispose());
        }
    }
}
