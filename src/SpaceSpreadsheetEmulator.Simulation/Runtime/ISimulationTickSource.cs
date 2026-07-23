namespace SpaceSpreadsheetEmulator.Simulation.Runtime;

/// <summary>
/// Supplies cancellable simulation ticks from an injectable source of time.
/// </summary>
public interface ISimulationTickSource : IAsyncDisposable
{
    ValueTask<bool> WaitForNextTickAsync(CancellationToken cancellationToken);
}
