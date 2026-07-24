using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Options;
using SpaceSpreadsheetEmulator.Backplane.Contracts.V1;
using SpaceSpreadsheetEmulator.Backplane.Contracts.V2;
using SpaceSpreadsheetEmulator.Cluster.Contracts.V1;
using ContractVector3 = SpaceSpreadsheetEmulator.Backplane.Contracts.V2.SolarVector3;

namespace SpaceSpreadsheetEmulator.Gateway.Backplane;

/// <summary>
/// Resolves ownership, forwards player intent, and consumes authoritative Worker streams.
/// </summary>
public sealed partial class GrpcSolarSystemBackend : ISolarSystemBackend, IDisposable
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
        routes[solarSystemId] = new CachedRoute(route, now.AddSeconds(options.RouteCacheSeconds));
        return route;
    }

    public Task<SolarSystemTransition?> RequestUndockAsync(
        SolarSystemRoute route,
        ulong gatewaySessionId,
        ReadOnlyMemory<byte> loginTicket,
        CharacterSummary character,
        int stationId,
        long clientCallId,
        CancellationToken cancellationToken)
        => RequestTransitionAsync(
            route,
            gatewaySessionId,
            loginTicket,
            character,
            stationId,
            clientCallId,
            dock: false,
            cancellationToken);

    public Task<SolarSystemTransition?> RequestDockAsync(
        SolarSystemRoute route,
        ulong gatewaySessionId,
        ReadOnlyMemory<byte> loginTicket,
        CharacterSummary character,
        int stationId,
        long clientCallId,
        CancellationToken cancellationToken)
        => RequestTransitionAsync(
            route,
            gatewaySessionId,
            loginTicket,
            character,
            stationId,
            clientCallId,
            dock: true,
            cancellationToken);

    public async Task<SolarSystemEntityState?> SetMovementIntentAsync(
        SolarSystemRoute route,
        ulong gatewaySessionId,
        ReadOnlyMemory<byte> loginTicket,
        CharacterSummary character,
        SolarSystemMovementIntent intent,
        CancellationToken cancellationToken)
    {
        WorkerConnection worker = GetWorker(route);
        var request = new MovementIntentRequest
        {
            Context = CreateContext(gatewaySessionId),
            LoginTicket = ByteString.CopyFrom(loginTicket.Span),
            OwnerNodeId = route.OwnerNodeId,
            ExpectedEpoch = route.Epoch,
            SolarSystemId = character.SolarSystemId,
            CharacterId = character.CharacterId,
            ShipId = character.ShipId,
            Kind = Map(intent.Kind),
            RequestedSpeed = intent.RequestedSpeed,
            TargetEntityId = intent.TargetEntityId ?? 0,
            DesiredRange = intent.DesiredRange,
        };
        if (intent.Kind is SolarSystemMovementIntentKind.Direction)
        {
            request.Direction = new ContractVector3
            {
                X = intent.DirectionX,
                Y = intent.DirectionY,
                Z = intent.DirectionZ,
            };
        }

        if (intent.Kind is SolarSystemMovementIntentKind.GoToPoint)
        {
            request.TargetPosition = new ContractVector3
            {
                X = intent.TargetPositionX!.Value,
                Y = intent.TargetPositionY!.Value,
                Z = intent.TargetPositionZ!.Value,
            };
        }
        try
        {
            SolarSystemCommandResult response = await worker.Client.SetMovementIntentAsync(
                request,
                cancellationToken: cancellationToken);
            return string.IsNullOrEmpty(response.Error?.Code) && response.ShipState is not null
                ? Map(response.ShipState)
                : null;
        }
        catch (RpcException error) when (IsRouteFailure(error, cancellationToken))
        {
            routes.TryRemove(route.SolarSystemId, out _);
            return null;
        }
    }

    private static MovementIntentKind Map(SolarSystemMovementIntentKind kind)
        => kind switch
        {
            SolarSystemMovementIntentKind.Direction => MovementIntentKind.MovementIntentDirection,
            SolarSystemMovementIntentKind.Stop => MovementIntentKind.MovementIntentStop,
            SolarSystemMovementIntentKind.Follow => MovementIntentKind.MovementIntentFollow,
            SolarSystemMovementIntentKind.Orbit => MovementIntentKind.MovementIntentOrbit,
            SolarSystemMovementIntentKind.GoToPoint => MovementIntentKind.MovementIntentGoToPoint,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    public void Dispose()
    {
        coordinatorChannel.Dispose();
        foreach (WorkerConnection worker in workers.Values)
        {
            worker.Channel.Dispose();
        }
    }

    private async Task<SolarSystemTransition?> RequestTransitionAsync(
        SolarSystemRoute route,
        ulong gatewaySessionId,
        ReadOnlyMemory<byte> loginTicket,
        CharacterSummary character,
        int stationId,
        long clientCallId,
        bool dock,
        CancellationToken cancellationToken)
    {
        WorkerConnection worker = GetWorker(route);
        var request = new SolarSystemTransitionIntent
        {
            Context = CreateContext(gatewaySessionId),
            LoginTicket = ByteString.CopyFrom(loginTicket.Span),
            OwnerNodeId = route.OwnerNodeId,
            ExpectedEpoch = route.Epoch,
            SolarSystemId = character.SolarSystemId,
            CharacterId = character.CharacterId,
            ShipId = character.ShipId,
            StationId = stationId,
            IdempotencyKey = CreateIdempotencyKey(
                gatewaySessionId,
                clientCallId,
                dock ? "dock" : "undock"),
        };
        SolarSystemCommandResult response;
        try
        {
            response = dock
                ? await worker.Client.RequestDockAsync(request, cancellationToken: cancellationToken)
                : await worker.Client.RequestUndockAsync(request, cancellationToken: cancellationToken);
        }
        catch (RpcException error) when (IsRouteFailure(error, cancellationToken))
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

    private WorkerConnection GetWorker(SolarSystemRoute route)
        => workers.GetOrAdd(
            route.Endpoint.AbsoluteUri,
            endpoint => WorkerConnection.Create(endpoint));

    private GameplayRequestContext CreateContext(ulong gatewaySessionId)
        => new()
        {
            GatewayId = options.GatewayId,
            GatewaySessionId = gatewaySessionId,
            CorrelationId = Guid.NewGuid().ToString("N"),
            ClientBuild = 3_396_210,
        };

    private string CreateIdempotencyKey(
        ulong gatewaySessionId,
        long clientCallId,
        string operation)
    {
        string material = string.Join(
            '\u001f',
            "sse-location-v2",
            options.GatewayId,
            gatewaySessionId.ToString(CultureInfo.InvariantCulture),
            clientCallId.ToString(CultureInfo.InvariantCulture),
            operation);
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(material)))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static bool IsRouteFailure(RpcException error, CancellationToken cancellationToken)
        => !cancellationToken.IsCancellationRequested
            && error.StatusCode is StatusCode.Unavailable
                or StatusCode.DeadlineExceeded
                or StatusCode.Cancelled;

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
