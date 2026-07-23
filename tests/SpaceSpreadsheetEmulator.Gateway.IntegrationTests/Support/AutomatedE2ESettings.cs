using System.Text.Json.Nodes;

namespace SpaceSpreadsheetEmulator.Gateway.IntegrationTests.Support;

internal static class AutomatedE2ESettings
{
    public static void ConfigureWorker(
        JsonObject settings,
        string artifactDirectory,
        string gameDatabaseConnectionString,
        int managementPort,
        Uri grpcAddress)
    {
        JsonObject endpoints = RequiredObject(RequiredObject(settings, "Kestrel"), "Endpoints");
        RequiredObject(endpoints, "Management")["Url"] = $"http://127.0.0.1:{managementPort}";
        RequiredObject(endpoints, "Backplane")["Url"] = grpcAddress.AbsoluteUri;

        settings["ConnectionStrings"] = new JsonObject
        {
            ["GameDatabase"] = gameDatabaseConnectionString,
        };
        RequiredObject(RequiredObject(settings, "Worker"), "Login")["ArtifactDirectory"] =
            artifactDirectory;
    }

    public static void ConfigureCoordinator(
        JsonObject settings,
        int managementPort,
        Uri grpcAddress,
        Uri workerGrpcAddress)
    {
        JsonObject endpoints = RequiredObject(RequiredObject(settings, "Kestrel"), "Endpoints");
        RequiredObject(endpoints, "Management")["Url"] = $"http://127.0.0.1:{managementPort}";
        RequiredObject(endpoints, "Grpc")["Url"] = grpcAddress.AbsoluteUri;

        JsonArray assignments = RequiredArray(
            RequiredObject(RequiredObject(settings, "Coordinator"), "BootstrapSolarSystems"),
            "Assignments");
        RequiredArrayObject(assignments, 0)["Endpoint"] = workerGrpcAddress.AbsoluteUri;
        RequiredArrayObject(assignments, 1)["Endpoint"] = workerGrpcAddress.AbsoluteUri;
    }

    public static void ConfigureGateway(
        JsonObject settings,
        int managementPort,
        int tcpPort,
        Uri coordinatorGrpcAddress,
        Uri workerGrpcAddress)
    {
        JsonObject endpoints = RequiredObject(RequiredObject(settings, "Kestrel"), "Endpoints");
        RequiredObject(endpoints, "Management")["Url"] = $"http://127.0.0.1:{managementPort}";

        JsonObject gateway = RequiredObject(settings, "Gateway");
        JsonObject backplane = RequiredObject(gateway, "Backplane");
        backplane["Address"] = workerGrpcAddress.AbsoluteUri;
        backplane["CoordinatorAddress"] = coordinatorGrpcAddress.AbsoluteUri;
        RequiredObject(gateway, "Tcp")["Port"] = tcpPort;
    }

    private static JsonObject RequiredObject(JsonObject parent, string propertyName)
        => parent[propertyName] as JsonObject
            ?? throw new InvalidDataException(
                $"Automated E2E appsettings is missing object '{propertyName}'.");

    private static JsonArray RequiredArray(JsonObject parent, string propertyName)
        => parent[propertyName] as JsonArray
            ?? throw new InvalidDataException(
                $"Automated E2E appsettings is missing array '{propertyName}'.");

    private static JsonObject RequiredArrayObject(JsonArray array, int index)
        => index < array.Count && array[index] is JsonObject item
            ? item
            : throw new InvalidDataException(
                $"Automated E2E appsettings is missing object at array index {index}.");
}
