using System.Threading.Channels;
using SpaceSpreadsheetEmulator.Primitives.Identifiers;

namespace SpaceSpreadsheetEmulator.Simulation.Runtime;

public sealed partial class SolarSystemRuntime
{
    private readonly Dictionary<Guid, SubscriptionRegistration> subscriptions = [];
    private readonly int sessionEventQueueCapacity;
    private ulong eventSequence;

    public Task<SolarSystemSubscription> SubscribeSessionAsync(
        CharacterId characterId,
        long shipId,
        SimulationEpoch expectedEpoch,
        CancellationToken cancellationToken = default)
        => SubmitAsync(
            new SubscribeSessionCommand(characterId, shipId, expectedEpoch, cancellationToken),
            cancellationToken);

    private SolarSystemSubscription Subscribe(
        CharacterId characterId,
        long shipId,
        SimulationEpoch expectedEpoch)
    {
        if (state.InspectShipState(characterId, shipId, expectedEpoch) is null)
        {
            throw new InvalidOperationException(
                "The subscribing character must be present in this solar system.");
        }

        Guid subscriptionId = Guid.NewGuid();
        Channel<SolarSystemEvent> events = Channel.CreateBounded<SolarSystemEvent>(
            new BoundedChannelOptions(sessionEventQueueCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true,
            });
        subscriptions.Add(
            subscriptionId,
            new SubscriptionRegistration(characterId, events));
        events.Writer.TryWrite(new SolarSystemSessionSnapshot(
            eventSequence,
            state.ListShipStates(expectedEpoch),
            state.ListStaticObjects(expectedEpoch)));
        return new SolarSystemSubscription(
            events.Reader,
            () => ReleaseSubscriptionAsync(subscriptionId));
    }

    private void Publish(Func<ulong, SolarSystemEvent> createEvent)
    {
        SolarSystemEvent item = createEvent(checked(++eventSequence));
        List<Guid>? lagging = null;
        foreach ((Guid subscriptionId, SubscriptionRegistration subscription) in subscriptions)
        {
            if (subscription.Events.Writer.TryWrite(item))
            {
                continue;
            }

            subscription.Events.Writer.TryComplete(new SolarSystemEventGapException());
            (lagging ??= []).Add(subscriptionId);
        }

        if (lagging is null)
        {
            return;
        }

        foreach (Guid subscriptionId in lagging)
        {
            subscriptions.Remove(subscriptionId);
        }
    }

    private void CompleteCharacterSubscription(CharacterId characterId)
    {
        foreach ((Guid subscriptionId, SubscriptionRegistration subscription) in
                 subscriptions.Where(pair => pair.Value.CharacterId == characterId).ToArray())
        {
            subscription.Events.Writer.TryComplete();
            subscriptions.Remove(subscriptionId);
        }
    }

    private void CompleteSubscriptions()
    {
        foreach (SubscriptionRegistration subscription in subscriptions.Values)
        {
            subscription.Events.Writer.TryComplete();
        }

        subscriptions.Clear();
    }

    private async ValueTask ReleaseSubscriptionAsync(Guid subscriptionId)
    {
        if (Status is SolarSystemRuntimeStatus.Stopped or SolarSystemRuntimeStatus.Faulted)
        {
            return;
        }

        try
        {
            await commands.Writer.WriteAsync(new UnsubscribeCommand(subscriptionId));
        }
        catch (ChannelClosedException)
        {
        }
    }

    private void RemoveSubscription(Guid subscriptionId)
    {
        if (subscriptions.Remove(subscriptionId, out SubscriptionRegistration? subscription))
        {
            subscription.Events.Writer.TryComplete();
        }
    }

    private sealed class SubscribeSessionCommand(
        CharacterId characterId,
        long shipId,
        SimulationEpoch expectedEpoch,
        CancellationToken cancellationToken)
        : ResultCommand<SolarSystemSubscription>(cancellationToken)
    {
        protected override SolarSystemSubscription Apply(SolarSystemRuntime runtime)
            => runtime.Subscribe(characterId, shipId, expectedEpoch);
    }

    private sealed class UnsubscribeCommand(Guid subscriptionId)
        : RuntimeCommand(CancellationToken.None)
    {
        public override void Execute(SolarSystemRuntime runtime)
            => runtime.RemoveSubscription(subscriptionId);

        public override void Cancel(CancellationToken cancellationToken)
        {
        }
    }

    private sealed record SubscriptionRegistration(
        CharacterId CharacterId,
        Channel<SolarSystemEvent> Events);
}
