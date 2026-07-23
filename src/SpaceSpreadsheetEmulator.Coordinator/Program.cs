using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using SpaceSpreadsheetEmulator.Cluster.Directory;
using SpaceSpreadsheetEmulator.Coordinator.Configuration;
using SpaceSpreadsheetEmulator.Coordinator.Services;
using SpaceSpreadsheetEmulator.Primitives.Identifiers;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();
builder.Services.AddHealthChecks();
builder.Services.AddOptions<CoordinatorBootstrapOptions>()
    .Bind(builder.Configuration.GetSection("Coordinator:BootstrapSolarSystems"))
    .Validate(options => options.HasValidAssignments(),
        "Bootstrap solar-system assignments require unique positive IDs, Worker identities, epochs, and absolute endpoints.")
    .ValidateOnStart();
builder.Services.AddSingleton<InMemoryPartitionDirectory>();
builder.Services.AddSingleton<IPartitionDirectory>(services =>
    services.GetRequiredService<InMemoryPartitionDirectory>());

var app = builder.Build();

CoordinatorBootstrapOptions bootstrap = builder.Configuration
    .GetSection("Coordinator:BootstrapSolarSystems")
    .Get<CoordinatorBootstrapOptions>() ?? new CoordinatorBootstrapOptions();
if (bootstrap.Enabled && !bootstrap.HasValidAssignments())
{
    throw new InvalidDataException("Coordinator bootstrap solar-system assignments are invalid.");
}

if (bootstrap.Enabled)
{
    InMemoryPartitionDirectory directory = app.Services.GetRequiredService<InMemoryPartitionDirectory>();
    foreach (CoordinatorBootstrapAssignmentOptions assignment in bootstrap.Assignments)
    {
        directory.Set(new PartitionAssignment(
            new PartitionKey(
                PartitionKind.SolarSystem,
                assignment.SolarSystemId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new NodeId(assignment.OwnerNodeId),
            new SimulationEpoch(assignment.Epoch),
            new Uri(assignment.Endpoint, UriKind.Absolute)));
    }
}

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready");
app.MapGrpcService<ClusterDirectoryGrpcService>();

app.Run();

public partial class Program;
