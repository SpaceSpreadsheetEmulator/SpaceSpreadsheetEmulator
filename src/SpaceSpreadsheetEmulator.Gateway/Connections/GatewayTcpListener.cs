using System.Collections.Concurrent;
using System.IO.Abstractions;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using SpaceSpreadsheetEmulator.Gateway.Backplane;
using SpaceSpreadsheetEmulator.Gateway.Compatibility;

namespace SpaceSpreadsheetEmulator.Gateway.Connections;

/// <summary>
/// Accepts bounded client TCP connections and gives each connection an isolated protocol session.
/// </summary>
public sealed partial class GatewayTcpListener(
    IOptions<GatewayConnectionOptions> options,
    GatewayConnectionMetrics metrics,
    ILoginBackend loginBackend,
    ISolarSystemBackend solarSystemBackend,
    IOptions<GatewayCompatibilityOptions> compatibilityOptions,
    IFileSystem fileSystem,
    TimeProvider timeProvider,
    ILoggerFactory loggerFactory,
    ILogger<GatewayTcpListener> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<long, Task> connections = new();
    private readonly CancellationTokenSource connectionCancellation = new();
    private readonly Build3396210StartupProfile startupProfile = new(compatibilityOptions.Value, fileSystem);
    private long nextConnectionId;
    private TcpListener? listener;

    public IPEndPoint? BoundEndpoint { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        GatewayConnectionOptions settings = options.Value;
        if (!settings.Enabled)
        {
            LogListenerDisabled(logger);
            return;
        }

        IPAddress address = IPAddress.Parse(settings.Address);
        listener = new TcpListener(address, settings.Port);
        listener.Start();
        BoundEndpoint = (IPEndPoint)listener.LocalEndpoint;
        using var slots = new SemaphoreSlim(settings.SoftConnectionLimit, settings.SoftConnectionLimit);

        LogListenerStarted(
            logger,
            BoundEndpoint,
            settings.SoftConnectionLimit);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                TcpClient client = await listener.AcceptTcpClientAsync(stoppingToken);
                if (!slots.Wait(0, CancellationToken.None))
                {
                    metrics.ConnectionRejected();
                    LogConnectionRejected(logger);
                    client.Dispose();
                    continue;
                }

                client.NoDelay = true;
                metrics.ConnectionOpened();
                long connectionId = Interlocked.Increment(ref nextConnectionId);
                var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                Task task = RunConnectionAsync(connectionId, client, slots, start.Task);
                if (!connections.TryAdd(connectionId, task))
                {
                    throw new InvalidOperationException("A duplicate connection identifier was allocated.");
                }

                start.SetResult();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (SocketException) when (stoppingToken.IsCancellationRequested || connectionCancellation.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (stoppingToken.IsCancellationRequested || connectionCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            listener.Stop();
            connectionCancellation.Cancel();
            await Task.WhenAll(connections.Values);
            BoundEndpoint = null;
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        listener?.Stop();
        connectionCancellation.Cancel();
        return base.StopAsync(cancellationToken);
    }

    private async Task RunConnectionAsync(
        long connectionId,
        TcpClient client,
        SemaphoreSlim slots,
        Task start)
    {
        await start;
        try
        {
            using var connection = new GatewayClientConnection(
                client,
                checked((ulong)connectionId),
                options,
                loginBackend,
                solarSystemBackend,
                startupProfile,
                timeProvider,
                loggerFactory.CreateLogger<GatewayClientConnection>());
            await connection.RunAsync(connectionCancellation.Token);
        }
        catch (OperationCanceledException) when (connectionCancellation.IsCancellationRequested)
        {
        }
        catch (IOException exception)
        {
            LogIoError(logger, exception);
        }
        catch (SocketException exception)
        {
            LogSocketError(logger, exception);
        }
        catch (Exception exception)
        {
            LogUnexpectedConnectionError(logger, connectionId, exception);
        }
        finally
        {
            client.Dispose();
            metrics.ConnectionClosed();
            slots.Release();
            connections.TryRemove(connectionId, out _);
        }
    }

    [LoggerMessage(EventId = 200, Level = LogLevel.Information, Message = "Gateway TCP listener is disabled")]
    private static partial void LogListenerDisabled(ILogger logger);

    [LoggerMessage(
        EventId = 201,
        Level = LogLevel.Information,
        Message = "Gateway TCP listener started on {Endpoint} with soft limit {SoftConnectionLimit}")]
    private static partial void LogListenerStarted(
        ILogger logger,
        IPEndPoint endpoint,
        int softConnectionLimit);

    [LoggerMessage(
        EventId = 202,
        Level = LogLevel.Information,
        Message = "Rejected connection because the Gateway soft limit is full")]
    private static partial void LogConnectionRejected(ILogger logger);

    [LoggerMessage(EventId = 203, Level = LogLevel.Debug, Message = "Client connection ended with an I/O error")]
    private static partial void LogIoError(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 204, Level = LogLevel.Debug, Message = "Client connection ended with a socket error")]
    private static partial void LogSocketError(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 205,
        Level = LogLevel.Error,
        Message = "Client connection {ConnectionId} ended with an unexpected error")]
    private static partial void LogUnexpectedConnectionError(
        ILogger logger,
        long connectionId,
        Exception exception);
}
