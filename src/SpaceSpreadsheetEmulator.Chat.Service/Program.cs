using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using SpaceSpreadsheetEmulator.Chat.Local;
using SpaceSpreadsheetEmulator.Chat.Service.Grpc;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddGrpc();
builder.Services.AddHealthChecks();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<LocalChatDirectory>();

var app = builder.Build();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready");
app.MapGrpcService<LocalChatGrpcService>();
app.Run();

public partial class Program;
