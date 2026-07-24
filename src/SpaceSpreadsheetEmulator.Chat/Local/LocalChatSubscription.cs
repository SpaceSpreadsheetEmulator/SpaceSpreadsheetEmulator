using System.Threading.Channels;

namespace SpaceSpreadsheetEmulator.Chat.Local;

public sealed class LocalChatSubscription(
    ChannelReader<LocalChatEvent> events,
    Action release) : IDisposable
{
    private int disposed;

    public IAsyncEnumerable<LocalChatEvent> ReadAllAsync(
        CancellationToken cancellationToken = default)
        => events.ReadAllAsync(cancellationToken);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
        {
            release();
        }
    }
}
