using System.Threading.Channels;
using SpaceSpreadsheetEmulator.Gateway.Connections;

namespace SpaceSpreadsheetEmulator.Gateway.IntegrationTests.Connections;

public sealed class GatewayOutboundSequencerTests
{
    [Fact]
    public async Task ConcurrentBatchesRemainContiguousUnderBackpressure()
    {
        Channel<OutboundFrame> channel = Channel.CreateBounded<OutboundFrame>(
            new BoundedChannelOptions(1)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
            });
        using var sequencer = new GatewayOutboundSequencer(channel.Writer);
        Task first = sequencer.EnqueueAsync(
            [Frame(1), Frame(2)],
            CancellationToken.None);
        Task second = sequencer.EnqueueAsync(
            [Frame(3), Frame(4)],
            CancellationToken.None);

        var received = new List<byte>();
        while (received.Count < 4)
        {
            received.Add((await channel.Reader.ReadAsync()).Payload[0]);
        }

        await Task.WhenAll(first, second);
        Assert.True(
            received.SequenceEqual(new byte[] { 1, 2, 3, 4 })
            || received.SequenceEqual(new byte[] { 3, 4, 1, 2 }));
    }

    private static OutboundFrame Frame(byte value)
        => new([value], Encrypt: true);
}
