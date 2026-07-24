using System.IO.Abstractions;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpaceSpreadsheetEmulator.Gateway.Backplane;
using SpaceSpreadsheetEmulator.Gateway.Compatibility;
using SpaceSpreadsheetEmulator.Gateway.Connections;

namespace SpaceSpreadsheetEmulator.Gateway.IntegrationTests.Support;

internal sealed class GatewayHostHarness : IAsyncDisposable
{
    private readonly IHost host;

    private GatewayHostHarness(
        IHost host,
        IPEndPoint endpoint,
        GatewayConnectionMetrics metrics,
        TestLoginBackend loginBackend,
        TestSolarSystemBackend solarBackend)
    {
        this.host = host;
        Endpoint = endpoint;
        Metrics = metrics;
        LoginBackend = loginBackend;
        SolarBackend = solarBackend;
    }

    public IPEndPoint Endpoint { get; }

    public GatewayConnectionMetrics Metrics { get; }

    public TestLoginBackend LoginBackend { get; }

    public TestSolarSystemBackend SolarBackend { get; }

    public static async Task<GatewayHostHarness> StartAsync(
        int softLimit,
        string capturedStartupDataDirectory = "")
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<IFileSystem>(new FileSystem());
        builder.Services.AddSingleton<IOptions<GatewayConnectionOptions>>(Options.Create(new GatewayConnectionOptions
        {
            Enabled = true,
            Address = IPAddress.Loopback.ToString(),
            Port = 0,
            SoftConnectionLimit = softLimit,
            OutboundQueueCapacity = 4,
        }));
        builder.Services.AddSingleton<TestLoginBackend>();
        builder.Services.AddSingleton<ILoginBackend>(
            services => services.GetRequiredService<TestLoginBackend>());
        builder.Services.AddSingleton<ISolarSystemBackend, TestSolarSystemBackend>();
        builder.Services.AddSingleton<IOptions<GatewayCompatibilityOptions>>(
            Options.Create(new GatewayCompatibilityOptions
            {
                CapturedStartupDataDirectory = capturedStartupDataDirectory,
            }));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<GatewayConnectionMetrics>();
        builder.Services.AddSingleton<GatewayTcpListener>();
        builder.Services.AddHostedService(services => services.GetRequiredService<GatewayTcpListener>());

        IHost host = builder.Build();
        await host.StartAsync();
        GatewayTcpListener listener = host.Services.GetRequiredService<GatewayTcpListener>();
        await WaitUntilAsync(() => listener.BoundEndpoint is not null);
        return new GatewayHostHarness(
            host,
            listener.BoundEndpoint!,
            host.Services.GetRequiredService<GatewayConnectionMetrics>(),
            host.Services.GetRequiredService<TestLoginBackend>(),
            (TestSolarSystemBackend)host.Services.GetRequiredService<ISolarSystemBackend>());
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
        => host.StopAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await host.StopAsync();
        host.Dispose();
    }

    public static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5), TimeProvider.System);
        while (!condition())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), TimeProvider.System, timeout.Token);
        }
    }
}
