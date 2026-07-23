using System.Collections.Concurrent;
using System.Globalization;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Options;
using SpaceSpreadsheetEmulator.Backplane.Contracts.V1;
using SpaceSpreadsheetEmulator.Cluster.Contracts.V1;

namespace SpaceSpreadsheetEmulator.Gateway.Backplane;

/// <summary>
/// Resolves solar-system ownership and forwards gameplay transitions over versioned gRPC contracts.
/// </summary>
public sealed class GrpcSolarSystemBackend : ISolarSystemBackend, IDisposable
{
    private const int SolarSystemPartitionKind = 1;
    private readonly GatewayBackplaneOptions options;
    private readonly TimeProvider timeProvider;
    private readonly GrpcChannel coordinatorChannel;
    private readonly ClusterDirectory.ClusterDirectoryClient directory;
    private readonly ConcurrentDictionary<int, CachedRoute> routes = new();
    private readonly ConcurrentDictionary<string, WorkerConnection> workers = new(StringComparer.Ordinal);

    public GrpcSolarSystemBackend(
        IOptions<GatewayBackplaneOptions> options,
        TimeProvider timeProvider)
    {
        this.options = options.Value;
        this.timeProvider = timeProvider;
        coordinatorChannel = GrpcChannel.ForAddress(this.options.CoordinatorAddress);
        directory = new ClusterDirectory.ClusterDirectoryClient(coordinatorChannel);
    }

    public async Task<SolarSystemRoute?> ResolveAsync(
        int solarSystemId,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        if (routes.TryGetValue(solarSystemId, out CachedRoute? cached) && cached.ExpiresAt > now)
        {
            return cached.Route;
        }

        ResolvePartitionResponse response;
        try
        {
            response = await directory.ResolvePartitionAsync(
                new ResolvePartitionRequest
                {
                    Kind = SolarSystemPartitionKind,
                    Key = solarSystemId.ToString(CultureInfo.InvariantCulture),
                },
                cancellationToken: cancellationToken);
        }
        catch (RpcException error) when (
            !cancellationToken.IsCancellationRequested
            && error.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded or StatusCode.Cancelled)
        {
            return null;
        }
        if (!response.Found
            || string.IsNullOrWhiteSpace(response.OwnerNodeId)
            || response.Epoch == 0
            || !Uri.TryCreate(response.Endpoint, UriKind.Absolute, out Uri? endpoint))
        {
            return null;
        }

        var route = new SolarSystemRoute(solarSystemId, response.OwnerNodeId, response.Epoch, endpoint);
        routes[solarSystemId] = new CachedRoute(
            route,
            now.AddSeconds(options.RouteCacheSeconds));
        return route;
    }

    public Task<SolarSystemTransition?> UndockAsync(
        SolarSystemRoute route,
        ulong gatewaySessionId,
        ReadOnlyMemory<byte> loginTicket,
        CharacterSummary character,
        CancellationToken cancellationToken)
        => MutateAsync(route, gatewaySessionId, loginTicket, character, dock: false, cancellationToken);

    public Task<SolarSystemTransition?> DockAsync(
        SolarSystemRoute route,
        ulong gatewaySessionId,
        ReadOnlyMemory<byte> loginTicket,
        CharacterSummary character,
        CancellationToken cancellationToken)
        => MutateAsync(route, gatewaySessionId, loginTicket, character, dock: true, cancellationToken);

    public void Dispose()
    {
        coordinatorChannel.Dispose();
        foreach (WorkerConnection worker in workers.Values)
        {
            worker.Channel.Dispose();
        }
    }

    private async Task<SolarSystemTransition?> MutateAsync(
        SolarSystemRoute route,
        ulong gatewaySessionId,
        ReadOnlyMemory<byte> loginTicket,
        CharacterSummary character,
        bool dock,
        CancellationToken cancellationToken)
    {
        WorkerConnection worker = workers.GetOrAdd(
            route.Endpoint.AbsoluteUri,
            endpoint => WorkerConnection.Create(endpoint));
        var request = new SolarSystemMutationRequest
        {
            Context = CreateContext(gatewaySessionId),
            LoginTicket = ByteString.CopyFrom(loginTicket.Span),
            OwnerNodeId = route.OwnerNodeId,
            ExpectedEpoch = route.Epoch,
            SolarSystemId = character.SolarSystemId,
            CharacterId = character.CharacterId,
            ShipId = character.ShipId,
            StationId = character.StationId,
        };
        SolarSystemMutationResponse response;
        try
        {
            response = dock
                ? await worker.Client.DockAsync(request, cancellationToken: cancellationToken)
                : await worker.Client.UndockAsync(request, cancellationToken: cancellationToken);
        }
        catch (RpcException error) when (
            !cancellationToken.IsCancellationRequested
            && error.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded or StatusCode.Cancelled)
        {
            routes.TryRemove(route.SolarSystemId, out _);
            return null;
        }
        if (!string.IsNullOrEmpty(response.Error?.Code))
        {
            routes.TryRemove(route.SolarSystemId, out _);
            return null;
        }

        return new SolarSystemTransition(
            response.SolarSystemId,
            response.CharacterId,
            response.ShipId,
            response.HasStationId ? response.StationId : null,
            response.Epoch);
    }

    private RequestContext CreateContext(ulong gatewaySessionId)
        => new()
        {
            GatewayId = options.GatewayId,
            GatewaySessionId = gatewaySessionId,
            CorrelationId = Guid.NewGuid().ToString("N"),
            ClientBuild = 3_396_210,
        };

    private sealed record CachedRoute(SolarSystemRoute Route, DateTimeOffset ExpiresAt);

    private sealed record WorkerConnection(
        GrpcChannel Channel,
        SolarSystemGameplay.SolarSystemGameplayClient Client)
    {
        public static WorkerConnection Create(string endpoint)
        {
            GrpcChannel channel = GrpcChannel.ForAddress(endpoint);
            return new WorkerConnection(channel, new SolarSystemGameplay.SolarSystemGameplayClient(channel));
        }
    }
}
