using System.Globalization;
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

    public static async Task<StandaloneTopology> StartAsync(string artifactDirectory)
    {
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
                "Worker",
                ProductionAssembly("Worker"),
                WorkerEnvironment(artifactDirectory, workerManagementPort, workerGrpcAddress)));
            await WaitForHealthAsync(
                topology.processes[^1],
                new Uri($"http://127.0.0.1:{workerManagementPort}/health/ready"));

            topology.processes.Add(ChildServerProcess.Start(
                "Coordinator",
                ProductionAssembly("Coordinator"),
                CoordinatorEnvironment(coordinatorManagementPort, coordinatorGrpcAddress, workerGrpcAddress)));
            await WaitForHealthAsync(
                topology.processes[^1],
                new Uri($"http://127.0.0.1:{coordinatorManagementPort}/health/ready"));

            topology.processes.Add(ChildServerProcess.Start(
                "Gateway",
                ProductionAssembly("Gateway"),
                GatewayEnvironment(
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

    private static IReadOnlyDictionary<string, string> WorkerEnvironment(
        string artifactDirectory,
        int managementPort,
        Uri grpcAddress)
        => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Kestrel__Endpoints__Management__Url"] = $"http://127.0.0.1:{managementPort}",
            ["Kestrel__Endpoints__Backplane__Url"] = grpcAddress.AbsoluteUri,
            ["Worker__Login__Enabled"] = bool.TrueString,
            ["Worker__Login__ArtifactDirectory"] = artifactDirectory,
            ["Worker__Login__DevelopmentEnrollmentEnabled"] = bool.TrueString,
            ["Worker__SolarSystem__Enabled"] = bool.TrueString,
            ["Worker__SolarSystem__NodeId"] = "worker-topology",
            ["Worker__SolarSystem__CommandQueueCapacity"] = "64",
            ["Worker__SolarSystem__Assignments__0__SolarSystemId"] = "30002780",
            ["Worker__SolarSystem__Assignments__0__Epoch"] = "7",
            ["Worker__SolarSystem__Assignments__0__EntryPoints__0__StationId"] = "60000004",
            ["Worker__SolarSystem__Assignments__0__EntryPoints__0__X"] = "100",
            ["Worker__SolarSystem__Assignments__0__EntryPoints__0__Y"] = "-50",
            ["Worker__SolarSystem__Assignments__0__EntryPoints__0__Z"] = "25",
            ["Worker__SolarSystem__Assignments__1__SolarSystemId"] = "30000142",
            ["Worker__SolarSystem__Assignments__1__Epoch"] = "9",
        };

    private static IReadOnlyDictionary<string, string> CoordinatorEnvironment(
        int managementPort,
        Uri grpcAddress,
        Uri workerGrpcAddress)
        => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Kestrel__Endpoints__Management__Url"] = $"http://127.0.0.1:{managementPort}",
            ["Kestrel__Endpoints__Grpc__Url"] = grpcAddress.AbsoluteUri,
            ["Coordinator__BootstrapSolarSystems__Enabled"] = bool.TrueString,
            ["Coordinator__BootstrapSolarSystems__Assignments__0__SolarSystemId"] = "30002780",
            ["Coordinator__BootstrapSolarSystems__Assignments__0__OwnerNodeId"] = "worker-topology",
            ["Coordinator__BootstrapSolarSystems__Assignments__0__Epoch"] = "7",
            ["Coordinator__BootstrapSolarSystems__Assignments__0__Endpoint"] = workerGrpcAddress.AbsoluteUri,
            ["Coordinator__BootstrapSolarSystems__Assignments__1__SolarSystemId"] = "30000142",
            ["Coordinator__BootstrapSolarSystems__Assignments__1__OwnerNodeId"] = "worker-topology",
            ["Coordinator__BootstrapSolarSystems__Assignments__1__Epoch"] = "9",
            ["Coordinator__BootstrapSolarSystems__Assignments__1__Endpoint"] = workerGrpcAddress.AbsoluteUri,
        };

    private static IReadOnlyDictionary<string, string> GatewayEnvironment(
        int managementPort,
        int tcpPort,
        Uri coordinatorGrpcAddress,
        Uri workerGrpcAddress)
        => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Kestrel__Endpoints__Management__Url"] = $"http://127.0.0.1:{managementPort}",
            ["Gateway__Backplane__Enabled"] = bool.TrueString,
            ["Gateway__Backplane__Address"] = workerGrpcAddress.AbsoluteUri,
            ["Gateway__Backplane__CoordinatorAddress"] = coordinatorGrpcAddress.AbsoluteUri,
            ["Gateway__Backplane__GatewayId"] = "gateway-topology",
            ["Gateway__Tcp__Enabled"] = bool.TrueString,
            ["Gateway__Tcp__Address"] = IPAddress.Loopback.ToString(),
            ["Gateway__Tcp__Port"] = tcpPort.ToString(CultureInfo.InvariantCulture),
        };

    private static string ProductionAssembly(string projectName)
    {
        string repositoryRoot = FindRepositoryRoot();
        var outputDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        string configuration = outputDirectory.Parent?.Name
            ?? throw new DirectoryNotFoundException("Could not determine the test build configuration.");
        return Path.Combine(
            repositoryRoot,
            "src",
            $"SpaceSpreadsheetEmulator.{projectName}",
            "bin",
            configuration,
            "net10.0",
            $"SpaceSpreadsheetEmulator.{projectName}.dll");
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not find the repository root.");
    }

    private static async Task WaitForHealthAsync(ChildServerProcess process, Uri endpoint)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(500) };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
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

            await Task.Delay(25, timeout.Token);
        }
    }

    private async Task WaitForGatewayAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            try
            {
                using TcpClient client = await LoopbackClient.ConnectAsync(GatewayEndpoint);
                return;
            }
            catch (SocketException)
            {
                await Task.Delay(25, timeout.Token);
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
