using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using System.IO.Abstractions;
using System.Net;
using SpaceSpreadsheetEmulator.Gateway.Backplane;
using SpaceSpreadsheetEmulator.Gateway.Compatibility;
using SpaceSpreadsheetEmulator.Gateway.Connections;
using SpaceSpreadsheetEmulator.Gateway.LocalEdge;

var builder = WebApplication.CreateBuilder(args);
IFileSystem fileSystem = new FileSystem();
builder.Services.AddSingleton(fileSystem);
builder.Configuration.AddJsonFile(
    $"appsettings.{builder.Environment.EnvironmentName}.local.json",
    optional: true,
    reloadOnChange: builder.Environment.IsDevelopment());

builder.Services.AddHealthChecks();
builder.Services.AddOptions<LocalClientEdgeOptions>()
    .Bind(builder.Configuration.GetSection("Gateway:LocalClientEdge"))
    .Validate(options => IPAddress.TryParse(options.Address, out IPAddress? address) && IPAddress.IsLoopback(address), "The local-client edge must use a loopback IP address.")
    .Validate(options => options.ProxyPort is > 0 and <= 65_535, "The local-client proxy port is outside its valid range.")
    .Validate(options => options.TlsPort is > 0 and <= 65_535, "The local-client TLS port is outside its valid range.")
    .Validate(options => options.ProxyPort != options.TlsPort, "The local-client proxy and TLS ports must differ.")
    .Validate(options => options.MaximumProxyConnections > 0, "The local-client proxy connection limit must be positive.")
    .Validate(options => !options.Enabled || !string.IsNullOrWhiteSpace(options.TrustDirectory), "The local-client trust directory is required when the edge is enabled.")
    .Validate(options => !options.Enabled || !string.IsNullOrWhiteSpace(options.GatewayCertificateDirectory), "The local-client Gateway certificate directory is required when the edge is enabled.")
    .ValidateOnStart();
builder.Services.AddOptions<GatewayBackplaneOptions>()
    .Bind(builder.Configuration.GetSection("Gateway:Backplane"))
    .Validate(options => Uri.TryCreate(options.Address, UriKind.Absolute, out _), "Gateway backplane address must be absolute.")
    .Validate(options => Uri.TryCreate(options.CoordinatorAddress, UriKind.Absolute, out _), "Gateway Coordinator address must be absolute.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.GatewayId), "Gateway identifier is required.")
    .Validate(options => options.RouteCacheSeconds > 0, "Gateway route-cache duration must be positive.")
    .ValidateOnStart();
builder.Services.AddOptions<GatewayConnectionOptions>()
    .Bind(builder.Configuration.GetSection("Gateway:Tcp"))
    .Validate(options => IPAddress.TryParse(options.Address, out _), "Gateway TCP address must be an IP address.")
    .Validate(options => options.Port is >= 0 and <= 65_535, "Gateway TCP port is outside its valid range.")
    .Validate(options => options.SoftConnectionLimit > 0, "Gateway soft connection limit must be positive.")
    .Validate(options => options.OutboundQueueCapacity > 0, "Gateway outbound queue capacity must be positive.")
    .ValidateOnStart();
builder.Services.AddOptions<GatewayCompatibilityOptions>()
    .Bind(builder.Configuration.GetSection("Gateway:Compatibility"))
    .Validate(
        options => string.IsNullOrWhiteSpace(options.CapturedStartupDataDirectory)
            || fileSystem.Directory.Exists(options.CapturedStartupDataDirectory),
        "The captured startup-data directory does not exist.")
    .ValidateOnStart();
builder.Services.AddSingleton<GatewayConnectionMetrics>();
TimeProvider timeProvider = TimeProvider.System;
builder.Services.AddSingleton(timeProvider);
GatewayBackplaneOptions backplaneOptions = builder.Configuration.GetSection("Gateway:Backplane").Get<GatewayBackplaneOptions>()
    ?? new GatewayBackplaneOptions();
if (backplaneOptions.Enabled)
{
    builder.Services.AddSingleton<ILoginBackend, GrpcLoginBackend>();
    builder.Services.AddSingleton<ISolarSystemBackend, GrpcSolarSystemBackend>();
}
else
{
    builder.Services.AddSingleton<ILoginBackend, UnavailableLoginBackend>();
    builder.Services.AddSingleton<ISolarSystemBackend, UnavailableSolarSystemBackend>();
}
builder.Services.AddSingleton<GatewayTcpListener>();
builder.Services.AddHostedService(services => services.GetRequiredService<GatewayTcpListener>());
builder.Services.AddHostedService<LocalEdgeProxy>();

LocalClientEdgeOptions localEdgeOptions = builder.Configuration.GetSection("Gateway:LocalClientEdge").Get<LocalClientEdgeOptions>()
    ?? new LocalClientEdgeOptions();
LocalEdgeCertificateSet? localEdgeCertificates = null;
if (localEdgeOptions.Enabled)
{
    var certificateProvisioner = new DevelopmentCertificateProvisioner(fileSystem, timeProvider);
    localEdgeCertificates = certificateProvisioner.Ensure(localEdgeOptions);
    builder.WebHost.ConfigureKestrel(options =>
    {
        options.Listen(IPAddress.Parse(localEdgeOptions.Address), localEdgeOptions.TlsPort, listenOptions =>
        {
            listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
            listenOptions.UseHttps(localEdgeCertificates.GatewayCertificate);
        });
    });
}

var app = builder.Build();

if (localEdgeCertificates is not null)
{
    app.Lifetime.ApplicationStopped.Register(localEdgeCertificates.GatewayCertificate.Dispose);
    app.MapPublicGateway();
}

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready");

app.Run();

public partial class Program;
