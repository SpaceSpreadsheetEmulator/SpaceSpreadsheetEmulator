using SpaceSpreadsheetEmulator.Backplane.Contracts.V1;
using SpaceSpreadsheetEmulator.Gateway.Backplane;

namespace SpaceSpreadsheetEmulator.Gateway.IntegrationTests.Support;

internal sealed class TestSolarSystemBackend : ISolarSystemBackend
{
    private TaskCompletionSource streamCompleted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int UndockCount { get; private set; }

    public int DockCount { get; private set; }

    public int SubscribeCount { get; private set; }

    public int SubscriptionClosedCount { get; private set; }

    public int MovementIntentCount { get; private set; }

    public List<SolarSystemMovementIntent> MovementIntents { get; } = [];

    public Task<SolarSystemRoute?> ResolveAsync(
        int solarSystemId,
        CancellationToken cancellationToken)
        => Task.FromResult<SolarSystemRoute?>(new SolarSystemRoute(
            solarSystemId,
            "worker-test",
            7,
            new Uri("http://127.0.0.1:7000")));

    public Task<SolarSystemTransition?> RequestUndockAsync(
        SolarSystemRoute route,
        ulong gatewaySessionId,
        ReadOnlyMemory<byte> loginTicket,
        CharacterSummary character,
        int stationId,
        long clientCallId,
        CancellationToken cancellationToken)
    {
        UndockCount++;
        if (streamCompleted.Task.IsCompleted)
        {
            streamCompleted = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        return Task.FromResult<SolarSystemTransition?>(new SolarSystemTransition(
            character.SolarSystemId,
            character.CharacterId,
            character.ShipId,
            null,
            route.Epoch));
    }

    public Task<SolarSystemTransition?> RequestDockAsync(
        SolarSystemRoute route,
        ulong gatewaySessionId,
        ReadOnlyMemory<byte> loginTicket,
        CharacterSummary character,
        int stationId,
        long clientCallId,
        CancellationToken cancellationToken)
    {
        DockCount++;
        streamCompleted.TrySetResult();
        return Task.FromResult<SolarSystemTransition?>(new SolarSystemTransition(
            character.SolarSystemId,
            character.CharacterId,
            character.ShipId,
            stationId,
            route.Epoch));
    }

    public Task<SolarSystemEntityState?> SetMovementIntentAsync(
        SolarSystemRoute route,
        ulong gatewaySessionId,
        ReadOnlyMemory<byte> loginTicket,
        CharacterSummary character,
        SolarSystemMovementIntent intent,
        CancellationToken cancellationToken)
    {
        MovementIntentCount++;
        MovementIntents.Add(intent);
        return Task.FromResult<SolarSystemEntityState?>(new SolarSystemEntityState(
            character.CharacterId,
            character.ShipId,
            route.SolarSystemId,
            route.Epoch,
            2,
            101,
            -50,
            25,
            0,
            0,
            0));
    }

    public async IAsyncEnumerable<SolarSystemSessionEvent> SubscribeSessionAsync(
        SolarSystemRoute route,
        ulong gatewaySessionId,
        ReadOnlyMemory<byte> loginTicket,
        CharacterSummary character,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        SubscribeCount++;
        try
        {
            yield return new SolarSystemSessionEvent(
                SolarSystemSessionEventKind.Snapshot,
                "gateway-test",
                gatewaySessionId,
                route.OwnerNodeId,
                route.SolarSystemId,
                route.Epoch,
                0,
                [
                    new SolarSystemEntityState(
                        character.CharacterId,
                        character.ShipId,
                        route.SolarSystemId,
                        route.Epoch,
                        1,
                        100,
                        -50,
                        25,
                        0,
                        0,
                        0),
                ],
                null,
                null,
                null);
            await streamCompleted.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            SubscriptionClosedCount++;
        }
    }
}
