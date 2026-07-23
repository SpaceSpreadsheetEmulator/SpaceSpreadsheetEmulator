using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Options;

namespace SpaceSpreadsheetEmulator.Gateway.LocalEdge;

/// <summary>
/// Proxies an explicit allowlist of local client HTTPS connections to the loopback compatibility endpoint.
/// </summary>
public sealed partial class LocalEdgeProxy(
    IOptions<LocalClientEdgeOptions> options,
    ILogger<LocalEdgeProxy> logger) : BackgroundService
{
    private const int MaximumHeaderBytes = 16 * 1024;
    private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "app.launchdarkly.com",
        "clientstream.launchdarkly.com",
        "clientsdk.launchdarkly.com",
        "dev-public-gateway.evetech.net",
        "events.launchdarkly.com",
        "public-gateway.evetech.net",
        "stream.launchdarkly.com",
    };

    private readonly ConcurrentDictionary<long, Task> connections = new();
    private TcpListener? listener;
    private long nextConnectionId;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LocalClientEdgeOptions settings = options.Value;
        if (!settings.Enabled)
        {
            return;
        }

        listener = new TcpListener(IPAddress.Parse(settings.Address), settings.ProxyPort);
        listener.Start();
        using var slots = new SemaphoreSlim(settings.MaximumProxyConnections, settings.MaximumProxyConnections);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                TcpClient client = await listener.AcceptTcpClientAsync(stoppingToken);
                if (!slots.Wait(0, stoppingToken))
                {
                    client.Dispose();
                    continue;
                }

                long id = Interlocked.Increment(ref nextConnectionId);
                Task task = HandleConnectionAsync(client, slots, stoppingToken);
                connections.TryAdd(id, task);
                _ = ObserveConnectionAsync(id, task);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (SocketException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            listener.Stop();
            await Task.WhenAll(connections.Values);
        }
    }

    private async Task HandleConnectionAsync(
        TcpClient client,
        SemaphoreSlim slots,
        CancellationToken cancellationToken)
    {
        using (client)
        {
            try
            {
                NetworkStream clientStream = client.GetStream();
                ProxyRequest request = await ReadRequestAsync(clientStream, cancellationToken);
                if (request.Method == "GET" && request.Target == "/health")
                {
                    const string body = "{\"status\":\"ok\",\"service\":\"sse\"}\n";
                    await WriteResponseAsync(
                        clientStream,
                        $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}",
                        cancellationToken);
                    return;
                }

                if (request.Method != "CONNECT"
                    || !TryParseAuthority(request.Target, out string? host, out int port)
                    || port != 443
                    || !AllowedHosts.Contains(host!))
                {
                    await WriteResponseAsync(clientStream, "HTTP/1.1 403 Forbidden\r\nContent-Length: 0\r\nConnection: close\r\n\r\n", cancellationToken);
                    LogBlockedTarget(logger, request.Target);
                    return;
                }

                using var upstream = new TcpClient();
                await upstream.ConnectAsync(IPAddress.Parse(options.Value.Address), options.Value.TlsPort, cancellationToken);
                NetworkStream upstreamStream = upstream.GetStream();
                await WriteResponseAsync(clientStream, "HTTP/1.1 200 Connection Established\r\n\r\n", cancellationToken);
                if (!request.TrailingBytes.IsEmpty)
                {
                    await upstreamStream.WriteAsync(request.TrailingBytes, cancellationToken);
                }

                Task upload = clientStream.CopyToAsync(upstreamStream, cancellationToken);
                Task download = upstreamStream.CopyToAsync(clientStream, cancellationToken);
                await Task.WhenAny(upload, download);
            }
            catch (Exception exception) when (exception is IOException or SocketException or InvalidDataException or OperationCanceledException)
            {
                if (logger.IsEnabled(LogLevel.Debug))
                {
                    string reason = exception switch
                    {
                        SocketException => nameof(SocketException),
                        IOException => nameof(IOException),
                        InvalidDataException => nameof(InvalidDataException),
                        OperationCanceledException => nameof(OperationCanceledException),
                        _ => "ConnectionError",
                    };
                    LogProxyConnectionEnded(logger, reason);
                }
            }
            finally
            {
                slots.Release();
            }
        }
    }

    private static async Task<ProxyRequest> ReadRequestAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[MaximumHeaderBytes];
        int length = 0;
        while (length < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken);
            if (read == 0)
            {
                throw new InvalidDataException("The proxy request ended before its headers.");
            }

            length += read;
            int marker = buffer.AsSpan(0, length).IndexOf("\r\n\r\n"u8);
            if (marker < 0)
            {
                continue;
            }

            int headerLength = marker + 4;
            string header = Encoding.ASCII.GetString(buffer, 0, headerLength);
            string requestLine = header.Split("\r\n", 2, StringSplitOptions.None)[0];
            string[] parts = requestLine.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3 || !parts[2].StartsWith("HTTP/1.", StringComparison.Ordinal))
            {
                throw new InvalidDataException("The proxy request line is malformed.");
            }

            return new ProxyRequest(parts[0], parts[1], buffer.AsMemory(headerLength, length - headerLength).ToArray());
        }

        throw new InvalidDataException("The proxy request headers exceed the configured limit.");
    }

    private static bool TryParseAuthority(string value, out string? host, out int port)
    {
        host = null;
        port = 0;
        int separator = value.LastIndexOf(':');
        if (separator <= 0 || !int.TryParse(value[(separator + 1)..], out port))
        {
            return false;
        }

        host = value[..separator];
        return host.Length > 0;
    }

    private static Task WriteResponseAsync(NetworkStream stream, string response, CancellationToken cancellationToken)
        => stream.WriteAsync(Encoding.ASCII.GetBytes(response), cancellationToken).AsTask();

    private async Task ObserveConnectionAsync(long id, Task task)
    {
        try
        {
            await task;
        }
        finally
        {
            connections.TryRemove(id, out _);
        }
    }

    [LoggerMessage(EventId = 400, Level = LogLevel.Information, Message = "Blocked local proxy target {Target}")]
    private static partial void LogBlockedTarget(ILogger logger, string target);

    [LoggerMessage(EventId = 401, Level = LogLevel.Debug, Message = "Local proxy connection ended with {Reason}")]
    private static partial void LogProxyConnectionEnded(ILogger logger, string reason);

    private sealed record ProxyRequest(string Method, string Target, ReadOnlyMemory<byte> TrailingBytes);
}
