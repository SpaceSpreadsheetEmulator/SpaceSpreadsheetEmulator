using System.Net;
using System.Net.Sockets;

namespace SpaceSpreadsheetEmulator.Gateway.IntegrationTests.Support;

internal static class LoopbackClient
{
    public static async Task<TcpClient> ConnectAsync(IPEndPoint endpoint)
    {
        var client = new TcpClient(AddressFamily.InterNetwork) { NoDelay = true };
        await client.ConnectAsync(endpoint.Address, endpoint.Port);
        return client;
    }

    public static async Task<byte[]> ReadExactlyAsync(NetworkStream stream, int count)
    {
        byte[] buffer = new byte[count];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5), TimeProvider.System);
        await stream.ReadExactlyAsync(buffer, timeout.Token);
        return buffer;
    }

    public static async Task<int> ReadUntilClosedAsync(NetworkStream stream)
    {
        byte[] buffer = new byte[1];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5), TimeProvider.System);
        return await stream.ReadAsync(buffer, timeout.Token);
    }
}
