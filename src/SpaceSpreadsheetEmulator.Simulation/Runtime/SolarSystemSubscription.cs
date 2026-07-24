using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace SpaceSpreadsheetEmulator.Simulation.Runtime;

/// <summary>
/// Owns one bounded, cancellable view of ordered solar-system events.
/// </summary>
public sealed class SolarSystemSubscription : IAsyncDisposable
{
    private readonly ChannelReader<SolarSystemEvent> events;
    private readonly Func<ValueTask> release;
    private int released;

    internal SolarSystemSubscription(
        ChannelReader<SolarSystemEvent> events,
        Func<ValueTask> release)
    {
        this.events = events;
        this.release = release;
    }

    public async IAsyncEnumerable<SolarSystemEvent> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (SolarSystemEvent item in events.ReadAllAsync(cancellationToken))
        {
            yield return item;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref released, 1) == 0)
        {
            await release();
        }
    }
}
