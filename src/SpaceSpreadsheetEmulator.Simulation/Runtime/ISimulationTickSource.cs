namespace SpaceSpreadsheetEmulator.Simulation.Runtime;

public interface ISimulationTickSource : IAsyncDisposable
{
    ValueTask<bool> WaitForNextTickAsync(CancellationToken cancellationToken);
}
