using SpaceSpreadsheetEmulator.Gateway.Backplane;

namespace SpaceSpreadsheetEmulator.Gateway.Connections;

internal sealed partial class GatewayClientConnection
{
    private async Task StartSolarSystemSubscriptionAsync(
        SolarSystemRoute route,
        CancellationToken cancellationToken)
    {
        await StopSolarSystemSubscriptionAsync(cancel: true);
        CancellationTokenSource subscriptionCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        solarSubscriptionCancellation = subscriptionCancellation;
        solarSubscriptionTask = ObserveSolarSystemAsync(
            route,
            subscriptionCancellation.Token);
    }

    private async Task ObserveSolarSystemAsync(
        SolarSystemRoute route,
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
                               selectedCharacter!,
                               cancellationToken))
            {
                ValidateSolarSystemEvent(
                    item,
                    route,
                    receivedSnapshot,
                    previousSequence);
                receivedSnapshot = true;
                previousSequence = item.Sequence;
                LogSolarSystemEvent(
                    logger,
                    item.Kind,
                    item.SolarSystemId,
                    item.Epoch,
                    item.Sequence);
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

        if (receivedSnapshot && item.Sequence <= previousSequence)
        {
            throw new InvalidDataException(
                "Worker emitted an out-of-order solar-system event.");
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
