using System.Threading.Channels;

namespace SpaceSpreadsheetEmulator.Gateway.Connections;

/// <summary>
/// Serializes causally related outbound frames from RPC and simulation producers.
/// </summary>
internal sealed class GatewayOutboundSequencer(
    ChannelWriter<OutboundFrame> writer) : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task EnqueueAsync(
        IReadOnlyList<OutboundFrame> frames,
        CancellationToken cancellationToken)
    {
        if (frames.Count == 0)
        {
            return;
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            foreach (OutboundFrame frame in frames)
            {
                await writer.WriteAsync(frame, cancellationToken);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose() => gate.Dispose();
}
