using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using SpaceSpreadsheetEmulator.Cluster.Directory;
using SpaceSpreadsheetEmulator.Coordinator.Configuration;
using SpaceSpreadsheetEmulator.Coordinator.Services;
using SpaceSpreadsheetEmulator.Primitives.Identifiers;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();
builder.Services.AddHealthChecks();
builder.Services.AddOptions<CoordinatorBootstrapOptions>()
    .Bind(builder.Configuration.GetSection("Coordinator:BootstrapSolarSystem"))
    .Validate(options => !options.Enabled || options.SolarSystemId > 0,
        "The bootstrap solar-system identifier must be positive.")
    .Validate(options => !options.Enabled || !string.IsNullOrWhiteSpace(options.OwnerNodeId),
        "The bootstrap Worker node identifier is required.")
    .Validate(options => !options.Enabled || options.Epoch > 0,
        "The bootstrap ownership epoch must be positive.")
    .Validate(options => !options.Enabled || Uri.TryCreate(options.Endpoint, UriKind.Absolute, out _),
        "The bootstrap Worker endpoint must be absolute.")
    .ValidateOnStart();
builder.Services.AddSingleton<InMemoryPartitionDirectory>();
builder.Services.AddSingleton<IPartitionDirectory>(services =>
    services.GetRequiredService<InMemoryPartitionDirectory>());

var app = builder.Build();

CoordinatorBootstrapOptions bootstrap = builder.Configuration
    .GetSection("Coordinator:BootstrapSolarSystem")
    .Get<CoordinatorBootstrapOptions>() ?? new CoordinatorBootstrapOptions();
if (bootstrap.Enabled)
{
    app.Services.GetRequiredService<InMemoryPartitionDirectory>().Set(new PartitionAssignment(
        new PartitionKey(PartitionKind.SolarSystem, bootstrap.SolarSystemId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        new NodeId(bootstrap.OwnerNodeId),
        new SimulationEpoch(bootstrap.Epoch),
        new Uri(bootstrap.Endpoint, UriKind.Absolute)));
}

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready");
app.MapGrpcService<ClusterDirectoryGrpcService>();

app.Run();

public partial class Program;
