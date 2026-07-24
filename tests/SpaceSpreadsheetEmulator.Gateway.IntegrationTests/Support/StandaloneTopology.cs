using System.IO.Abstractions;
using System.Net;
using System.Net.Sockets;

namespace SpaceSpreadsheetEmulator.Gateway.IntegrationTests.Support;

internal sealed class StandaloneTopology : IAsyncDisposable
{
    private readonly List<ChildServerProcess> processes = [];

    private StandaloneTopology(
        IPEndPoint gatewayEndpoint,
        Uri workerGrpcAddress,
        Uri coordinatorGrpcAddress)
    {
        GatewayEndpoint = gatewayEndpoint;
        WorkerGrpcAddress = workerGrpcAddress;
        CoordinatorGrpcAddress = coordinatorGrpcAddress;
    }

    public IPEndPoint GatewayEndpoint { get; }

    public Uri WorkerGrpcAddress { get; }

    public Uri CoordinatorGrpcAddress { get; }

    public static async Task<StandaloneTopology> StartAsync(
        IFileSystem fileSystem,
        string artifactDirectory,
        string gameDatabaseConnectionString)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        int workerManagementPort = ReservePort();
        int workerGrpcPort = ReservePort();
        int coordinatorManagementPort = ReservePort();
        int coordinatorGrpcPort = ReservePort();
        int gatewayManagementPort = ReservePort();
        int gatewayTcpPort = ReservePort();
        var workerGrpcAddress = new Uri($"http://127.0.0.1:{workerGrpcPort}");
        var coordinatorGrpcAddress = new Uri($"http://127.0.0.1:{coordinatorGrpcPort}");
        var topology = new StandaloneTopology(
            new IPEndPoint(IPAddress.Loopback, gatewayTcpPort),
            workerGrpcAddress,
            coordinatorGrpcAddress);

        try
        {
            topology.processes.Add(ChildServerProcess.Start(
                fileSystem,
                "Worker",
                ProductionAssembly(fileSystem, "Worker"),
                "AutomatedE2E",
                settings => AutomatedE2ESettings.ConfigureWorker(
                    settings,
                    artifactDirectory,
                    gameDatabaseConnectionString,
                    workerManagementPort,
                    workerGrpcAddress)));
            await WaitForHealthAsync(
                topology.processes[^1],
                new Uri($"http://127.0.0.1:{workerManagementPort}/health/ready"));

            topology.processes.Add(ChildServerProcess.Start(
                fileSystem,
                "Coordinator",
                ProductionAssembly(fileSystem, "Coordinator"),
                "AutomatedE2E",
                settings => AutomatedE2ESettings.ConfigureCoordinator(
                    settings,
                    coordinatorManagementPort,
                    coordinatorGrpcAddress,
                    workerGrpcAddress)));
            await WaitForHealthAsync(
                topology.processes[^1],
                new Uri($"http://127.0.0.1:{coordinatorManagementPort}/health/ready"));

            topology.processes.Add(ChildServerProcess.Start(
                fileSystem,
                "Gateway",
                ProductionAssembly(fileSystem, "Gateway"),
                "AutomatedE2E",
                settings => AutomatedE2ESettings.ConfigureGateway(
                    settings,
                    gatewayManagementPort,
                    gatewayTcpPort,
                    coordinatorGrpcAddress,
                    workerGrpcAddress)));
            await WaitForHealthAsync(
                topology.processes[^1],
                new Uri($"http://127.0.0.1:{gatewayManagementPort}/health/ready"));
            await topology.WaitForGatewayAsync();
            return topology;
        }
        catch
        {
            await topology.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        for (int index = processes.Count - 1; index >= 0; index--)
        {
            await processes[index].DisposeAsync();
        }

        processes.Clear();
    }

    private static string ProductionAssembly(IFileSystem fileSystem, string projectName)
    {
        string repositoryRoot = FindRepositoryRoot(fileSystem);
        IDirectoryInfo outputDirectory = fileSystem.DirectoryInfo.New(AppContext.BaseDirectory);
        string configuration = outputDirectory.Parent?.Name
            ?? throw new DirectoryNotFoundException("Could not determine the test build configuration.");
        return fileSystem.Path.Combine(
            repositoryRoot,
            "src",
            $"SpaceSpreadsheetEmulator.{projectName}",
            "bin",
            configuration,
            "net10.0",
            $"SpaceSpreadsheetEmulator.{projectName}.dll");
    }

    private static string FindRepositoryRoot(IFileSystem fileSystem)
    {
        for (IDirectoryInfo? directory = fileSystem.DirectoryInfo.New(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (fileSystem.Directory.Exists(fileSystem.Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not find the repository root.");
    }

    private static async Task WaitForHealthAsync(ChildServerProcess process, Uri endpoint)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(500) };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15), TimeProvider.System);
        while (true)
        {
            if (process.HasExited)
            {
                throw new InvalidOperationException(
                    $"{process.Name} exited before becoming ready.{Environment.NewLine}{process.DescribeLogs()}");
            }

            try
            {
                using HttpResponseMessage response = await client.GetAsync(endpoint, timeout.Token);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException) when (!timeout.IsCancellationRequested)
            {
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), TimeProvider.System, timeout.Token);
        }
    }

    private async Task WaitForGatewayAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10), TimeProvider.System);
        while (true)
        {
            try
            {
                using TcpClient client = await LoopbackClient.ConnectAsync(GatewayEndpoint);
                return;
            }
            catch (SocketException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25), TimeProvider.System, timeout.Token);
            }
        }
    }

    private static int ReservePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
