using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using SpaceSpreadsheetEmulator.Content.Characters;
using SpaceSpreadsheetEmulator.Gameplay.Characters;
using SpaceSpreadsheetEmulator.Identity.Authentication;
using SpaceSpreadsheetEmulator.StaticData;
using SpaceSpreadsheetEmulator.Worker.Login;
using SpaceSpreadsheetEmulator.Worker.Simulation;
using SpaceSpreadsheetEmulator.Primitives.Identifiers;
using SpaceSpreadsheetEmulator.Simulation.Runtime;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddGrpc();
builder.Services.AddHealthChecks();
builder.Services.AddOptions<WorkerLoginOptions>()
    .Bind(builder.Configuration.GetSection("Worker:Login"))
    .Validate(options => !options.Enabled || !string.IsNullOrWhiteSpace(options.ArtifactDirectory),
        "Worker login requires an immutable static-data artifact directory.")
    .Validate(options => options.MaximumAccounts > 0, "Maximum accounts must be positive.")
    .Validate(options => options.MaximumSessions > 0, "Maximum sessions must be positive.")
    .Validate(options => options.SessionLifetimeMinutes > 0, "Session lifetime must be positive.")
    .ValidateOnStart();
builder.Services.AddOptions<WorkerSolarSystemOptions>()
    .Bind(builder.Configuration.GetSection("Worker:SolarSystem"))
    .Validate(options => !options.Enabled || !string.IsNullOrWhiteSpace(options.NodeId),
        "Worker solar-system ownership requires a node identifier.")
    .Validate(options => !options.Enabled || options.SolarSystemId > 0,
        "Worker solar-system ownership requires a positive solar-system identifier.")
    .Validate(options => !options.Enabled || options.Epoch > 0,
        "Worker solar-system ownership requires a positive epoch.")
    .Validate(options => options.CommandQueueCapacity > 0,
        "Worker solar-system command queue capacity must be positive.")
    .ValidateOnStart();

WorkerLoginOptions loginOptions = builder.Configuration.GetSection("Worker:Login").Get<WorkerLoginOptions>()
    ?? new WorkerLoginOptions();
WorkerSolarSystemOptions solarOptions = builder.Configuration.GetSection("Worker:SolarSystem").Get<WorkerSolarSystemOptions>()
    ?? new WorkerSolarSystemOptions();
if (solarOptions.Enabled && !loginOptions.Enabled)
{
    throw new InvalidOperationException("The WIP solar-system slice requires Worker login sessions to be enabled.");
}

if (loginOptions.Enabled)
{
    SqliteStaticDataStore staticData = await SqliteStaticDataStore.OpenAsync(loginOptions.ArtifactDirectory);
    if (staticData.Compatibility is not { ClientBuild: 3_396_210, ProtocolProfile: 3_396_210, SdeBuild: 3_396_210 })
    {
        await staticData.DisposeAsync();
        throw new InvalidDataException("The Worker login artifact is not compatible with protocol profile 3396210.");
    }

    StarterCharacterTemplate template = StarterCharacterTemplate.Default;
    await template.ValidateAsync(staticData);
    builder.Services.AddSingleton<IStaticDataStore>(staticData);
    builder.Services.AddSingleton(template);
    builder.Services.AddSingleton<IAccountAuthenticator>(new InMemoryAccountAuthenticator(
        loginOptions.DevelopmentEnrollmentEnabled,
        loginOptions.MaximumAccounts));
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddSingleton<ICharacterSelectionQuery, CharacterSelectionQuery>();
    builder.Services.AddSingleton<LoginTicketRegistry>();
}

if (solarOptions.Enabled)
{
    builder.Services.AddSingleton<ISolarSystemRuntime>(new SolarSystemRuntime(
        new SolarSystemRuntimeContext(
            new SolarSystemId(solarOptions.SolarSystemId),
            new NodeId(solarOptions.NodeId),
            new SimulationEpoch(solarOptions.Epoch)),
        solarOptions.CommandQueueCapacity));
    builder.Services.AddHostedService<SolarSystemRuntimeHostedService>();
}

var app = builder.Build();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready");
if (loginOptions.Enabled)
{
    app.MapGrpcService<LoginGameplayGrpcService>();
}
if (solarOptions.Enabled)
{
    app.MapGrpcService<SolarSystemGameplayGrpcService>();
}

app.Run();

public partial class Program;
