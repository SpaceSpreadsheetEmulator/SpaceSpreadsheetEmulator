using System.Threading.Channels;
using SpaceSpreadsheetEmulator.Simulation.Runtime;

namespace SpaceSpreadsheetEmulator.Simulation.Tests.Runtime;

internal sealed class ManualSimulationTickSource : ISimulationTickSource
{
    private readonly Channel<bool> ticks = Channel.CreateUnbounded<bool>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
    });

    public void Advance() => ticks.Writer.TryWrite(true);

    public async ValueTask<bool> WaitForNextTickAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await ticks.Reader.ReadAsync(cancellationToken);
        }
        catch (ChannelClosedException)
        {
            return false;
        }
    }

    public ValueTask DisposeAsync()
    {
        ticks.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}
