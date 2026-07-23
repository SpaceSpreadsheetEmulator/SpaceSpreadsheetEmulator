namespace SpaceSpreadsheetEmulator.Simulation.Runtime;

/// <summary>
/// Produces simulation ticks at a fixed period using an injected time provider.
/// </summary>
public sealed class PeriodicSimulationTickSource : ISimulationTickSource
{
    private readonly PeriodicTimer timer;

    public PeriodicSimulationTickSource(TimeProvider timeProvider, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(period, TimeSpan.Zero);

        timer = new PeriodicTimer(period, timeProvider);
    }

    public ValueTask<bool> WaitForNextTickAsync(CancellationToken cancellationToken)
        => timer.WaitForNextTickAsync(cancellationToken);

    public ValueTask DisposeAsync()
    {
        timer.Dispose();
        return ValueTask.CompletedTask;
    }
}
