namespace SpaceSpreadsheetEmulator.Gateway.Connections;

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
