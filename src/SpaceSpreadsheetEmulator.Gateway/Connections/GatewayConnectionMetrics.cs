namespace SpaceSpreadsheetEmulator.Gateway.Connections;

/// <summary>
/// Tracks the current and rejected client connection counts for Gateway health reporting.
/// </summary>
public sealed class GatewayConnectionMetrics
{
    private int activeConnections;
    private long rejectedConnections;

    public int ActiveConnections => Volatile.Read(ref activeConnections);

    public long RejectedConnections => Interlocked.Read(ref rejectedConnections);

    internal void ConnectionOpened() => Interlocked.Increment(ref activeConnections);

    internal void ConnectionClosed() => Interlocked.Decrement(ref activeConnections);

    internal void ConnectionRejected() => Interlocked.Increment(ref rejectedConnections);
}
