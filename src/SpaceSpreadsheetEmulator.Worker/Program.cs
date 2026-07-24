using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using SpaceSpreadsheetEmulator.Content.Characters;
using SpaceSpreadsheetEmulator.Gameplay.Characters;
using SpaceSpreadsheetEmulator.Gameplay.Stations;
using SpaceSpreadsheetEmulator.Identity.Authentication;
using SpaceSpreadsheetEmulator.Persistence;
using SpaceSpreadsheetEmulator.Persistence.Database;
using SpaceSpreadsheetEmulator.StaticData;
using SpaceSpreadsheetEmulator.Worker.Login;
using SpaceSpreadsheetEmulator.Worker.Persistence;
using SpaceSpreadsheetEmulator.Worker.Simulation;
using SpaceSpreadsheetEmulator.Primitives.Identifiers;
using SpaceSpreadsheetEmulator.Simulation.Runtime;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile(
    $"appsettings.{builder.Environment.EnvironmentName}.local.json",
    optional: true,
    reloadOnChange: builder.Environment.IsDevelopment());
builder.Services.AddGrpc();
builder.Services.AddHealthChecks();
builder.Services.AddSingleton(TimeProvider.System);
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
    .Validate(options => options.CommandQueueCapacity > 0,
        "Worker solar-system command queue capacity must be positive.")
    .Validate(options => options.SessionEventQueueCapacity > 0,
        "Worker solar-system session-event queue capacity must be positive.")
    .Validate(options => options.CheckpointIntervalSeconds > 0,
        "Worker solar-system checkpoint interval must be positive.")
    .Validate(options => double.IsFinite(options.ManeuverSpeed) && options.ManeuverSpeed > 0,
        "Worker solar-system maneuver speed must be finite and positive.")
    .Validate(options => options.HasValidAssignments(),
        "Worker solar-system assignments require unique positive system IDs and epochs plus unique finite station entry points.")
    .ValidateOnStart();

WorkerLoginOptions loginOptions = builder.Configuration.GetSection("Worker:Login").Get<WorkerLoginOptions>()
    ?? new WorkerLoginOptions();
WorkerSolarSystemOptions solarOptions = builder.Configuration.GetSection("Worker:SolarSystem").Get<WorkerSolarSystemOptions>()
    ?? new WorkerSolarSystemOptions();
if (solarOptions.Enabled && !loginOptions.Enabled)
{
    throw new InvalidOperationException("The WIP solar-system slice requires Worker login sessions to be enabled.");
}

if (solarOptions.Enabled && !solarOptions.HasValidAssignments())
{
    throw new InvalidDataException("Worker solar-system assignments are invalid.");
}

if (loginOptions.Enabled)
{
    string connectionString = builder.Configuration.GetConnectionString("GameDatabase")
        ?? throw new InvalidOperationException(
            "A login-enabled Worker requires ConnectionStrings:GameDatabase.");
    builder.Services.AddGamePersistence(connectionString);
    builder.Services.AddHealthChecks()
        .AddCheck<GameDatabaseHealthCheck>("game-database");
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
    builder.Services.AddSingleton<IAccountAuthenticator>(services =>
        new InMemoryAccountAuthenticator(
            services.GetRequiredService<IAccountIdentityStore>(),
            loginOptions.DevelopmentEnrollmentEnabled,
            loginOptions.MaximumAccounts));
    builder.Services.AddSingleton<ICharacterSelectionQuery, CharacterSelectionQuery>();
    builder.Services.AddSingleton<IStationCatalogQuery, StationCatalogQuery>();
    builder.Services.AddSingleton<LoginTicketRegistry>();
}

if (solarOptions.Enabled)
{
    builder.Services.AddSingleton(new SolarSystemRuntimeRegistry());
    builder.Services.AddSingleton<ISolarSystemRuntimeRegistry>(
        services => services.GetRequiredService<SolarSystemRuntimeRegistry>());
    builder.Services.AddSingleton<ISolarSystemEntryPointResolver>(
        new ConfiguredSolarSystemEntryPointResolver(solarOptions.Assignments));
    builder.Services.AddSingleton<SolarSystemRuntimeInitializer>();
    builder.Services.AddSingleton<SolarSystemWorkflowCoordinator>();
    builder.Services.AddSingleton<SolarSystemRequestResolver>();
    builder.Services.AddHostedService<SolarSystemRuntimeHostedService>();
    builder.Services.AddHealthChecks()
        .AddCheck<SolarSystemRuntimeHealthCheck>("solar-system-runtimes");
}

var app = builder.Build();

if (loginOptions.Enabled)
{
    GameDatabaseReadiness readiness = await app.Services
        .GetRequiredService<IGameDatabaseReadinessProbe>()
        .CheckAsync();
    if (!readiness.IsReady)
    {
        throw new InvalidOperationException(readiness.Detail);
    }
}

if (solarOptions.Enabled)
{
    await app.Services.GetRequiredService<SolarSystemRuntimeInitializer>()
        .InitializeAsync();
}

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
