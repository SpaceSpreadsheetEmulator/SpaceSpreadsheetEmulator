using SpaceSpreadsheetEmulator.Backplane.Contracts.V1;
using SpaceSpreadsheetEmulator.Gateway.Backplane;
using SpaceSpreadsheetEmulator.Gateway.Compatibility;
using SpaceSpreadsheetEmulator.Protocol.MachoNet;

namespace SpaceSpreadsheetEmulator.Gateway.Connections;

internal sealed partial class GatewayClientConnection
{
    private readonly SemaphoreSlim solarOutputGate = new(1, 1);
    private bool solarOutputPaused;
    private ulong solarLastTick;

    private async Task StartSolarSystemSubscriptionAsync(
        SolarSystemRoute route,
        CancellationToken cancellationToken)
    {
        await StopSolarSystemSubscriptionAsync(cancel: true);
        solarOutputPaused = false;
        solarLastTick = 0;
        CharacterSummary character = selectedCharacter?.Clone()
            ?? throw new InvalidOperationException(
                "A character must be selected before opening a solar-system stream.");
        CancellationTokenSource subscriptionCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        solarSubscriptionCancellation = subscriptionCancellation;
        solarSubscriptionTask = ObserveSolarSystemAsync(
            route,
            character,
            subscriptionCancellation.Token);
    }

    private async Task ObserveSolarSystemAsync(
        SolarSystemRoute route,
        CharacterSummary character,
        CancellationToken cancellationToken)
    {
        bool receivedSnapshot = false;
        ulong previousSequence = 0;
        try
        {
            await foreach (SolarSystemSessionEvent item in solarSystemBackend.SubscribeSessionAsync(
                               route,
                               gatewaySessionId,
                               loginSession!.LoginTicket,
                               character,
                               cancellationToken))
            {
                ValidateSolarSystemEvent(
                    item,
                    route,
                    receivedSnapshot,
                    previousSequence);
                receivedSnapshot = true;
                previousSequence = item.Sequence;
                solarLastTick = LatestTick(item, solarLastTick);
                LogSolarSystemEvent(
                    logger,
                    item.Kind,
                    item.SolarSystemId,
                    item.Epoch,
                    item.Sequence);
                await solarOutputGate.WaitAsync(cancellationToken);
                try
                {
                    if (!solarOutputPaused)
                    {
                        MachoPacket? notification = MapSolarSystemEvent(item, character);
                        if (notification is not null)
                        {
                            GatewayOutboundSequencer outbound = outboundSequencer
                                ?? throw new InvalidOperationException(
                                    "The outbound sequencer is unavailable.");
                            await outbound.EnqueueAsync(
                                [CreatePacketFrame(notification)],
                                cancellationToken);
                        }
                    }
                }
                finally
                {
                    solarOutputGate.Release();
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            LogSolarSystemStreamFailed(logger, error);
            connectionLifetime?.Cancel();
        }
    }

    private async Task SetSolarOutputPausedAsync(
        bool paused,
        CancellationToken cancellationToken)
    {
        await solarOutputGate.WaitAsync(cancellationToken);
        try
        {
            solarOutputPaused = paused;
        }
        finally
        {
            solarOutputGate.Release();
        }
    }

    private async Task StopSolarSystemSubscriptionAsync(bool cancel)
    {
        CancellationTokenSource? subscriptionCancellation =
            solarSubscriptionCancellation;
        Task? subscriptionTask = solarSubscriptionTask;
        solarSubscriptionCancellation = null;
        solarSubscriptionTask = null;
        if (subscriptionCancellation is null || subscriptionTask is null)
        {
            return;
        }

        if (cancel)
        {
            await subscriptionCancellation.CancelAsync();
        }

        try
        {
            await subscriptionTask;
        }
        finally
        {
            subscriptionCancellation.Dispose();
        }
    }

    private void ValidateSolarSystemEvent(
        SolarSystemSessionEvent item,
        SolarSystemRoute route,
        bool receivedSnapshot,
        ulong previousSequence)
    {
        if (!string.Equals(item.OwnerNodeId, route.OwnerNodeId, StringComparison.Ordinal)
            || item.GatewaySessionId != gatewaySessionId
            || item.SolarSystemId != route.SolarSystemId
            || item.Epoch != route.Epoch)
        {
            throw new InvalidDataException(
                "Worker emitted a solar-system event for another route or session.");
        }

        if (!receivedSnapshot && item.Kind != SolarSystemSessionEventKind.Snapshot)
        {
            throw new InvalidDataException(
                "Worker emitted a solar-system delta before the initial snapshot.");
        }

        if (receivedSnapshot && item.Sequence != checked(previousSequence + 1))
        {
            throw new InvalidDataException(
                "Worker emitted a non-contiguous solar-system event sequence.");
        }

        foreach (SolarSystemEntityState entity in item.Snapshot)
        {
            ValidateEntity(entity, route);
        }

        foreach (SolarSystemStaticObjectState staticObject in item.StaticObjects ?? [])
        {
            if (staticObject.SolarSystemId != route.SolarSystemId)
            {
                throw new InvalidDataException(
                    "Worker emitted an authored object for another solar system.");
            }
        }

        if (item.Entity is not null)
        {
            ValidateEntity(item.Entity, route);
        }
    }

    private MachoPacket? MapSolarSystemEvent(
        SolarSystemSessionEvent item,
        CharacterSummary character)
        => item.Kind switch
        {
            SolarSystemSessionEventKind.Snapshot =>
                Build3396210SpaceSessionMapper.CreateSnapshot(
                    ProxyNodeId,
                    loginSession!.AccountId,
                    character,
                    item.Snapshot.Single(entity =>
                        entity.CharacterId == character.CharacterId
                        && entity.ShipId == character.ShipId),
                    item.StaticObjects ?? [],
                    timeProvider.GetUtcNow()),
            SolarSystemSessionEventKind.EntityEntered
                when item.Entity?.CharacterId == character.CharacterId =>
                Build3396210SpaceSessionMapper.CreateSnapshot(
                    ProxyNodeId,
                    loginSession!.AccountId,
                    character,
                    item.Entity,
                    [],
                    timeProvider.GetUtcNow()),
            SolarSystemSessionEventKind.EntityMoved
                or SolarSystemSessionEventKind.ShipStateChanged
                when item.Entity?.CharacterId == character.CharacterId =>
                Build3396210SpaceSessionMapper.CreateMovement(
                    ProxyNodeId,
                    loginSession!.AccountId,
                    item.Entity),
            SolarSystemSessionEventKind.EntityLeft
                when item.CharacterId == character.CharacterId
                     && item.ShipId == character.ShipId =>
                Build3396210SpaceSessionMapper.CreateStop(
                    ProxyNodeId,
                    loginSession!.AccountId,
                    solarLastTick,
                    character.ShipId),
            _ => null,
        };

    private static ulong LatestTick(
        SolarSystemSessionEvent item,
        ulong previous)
        => item.Entity?.Tick
            ?? item.Snapshot.Select(entity => entity.Tick).DefaultIfEmpty(previous).Max();

    private static void ValidateEntity(
        SolarSystemEntityState entity,
        SolarSystemRoute route)
    {
        if (entity.SolarSystemId != route.SolarSystemId
            || entity.Epoch != route.Epoch)
        {
            throw new InvalidDataException(
                "Worker emitted entity state for another solar-system epoch.");
        }
    }

    [LoggerMessage(
        EventId = 107,
        Level = LogLevel.Debug,
        Message = "Received {EventKind} for solar system {SolarSystemId} at epoch {Epoch}, sequence {Sequence}")]
    private static partial void LogSolarSystemEvent(
        ILogger logger,
        SolarSystemSessionEventKind eventKind,
        int solarSystemId,
        ulong epoch,
        ulong sequence);

    [LoggerMessage(
        EventId = 108,
        Level = LogLevel.Warning,
        Message = "Solar-system session stream failed")]
    private static partial void LogSolarSystemStreamFailed(
        ILogger logger,
        Exception exception);
}
