using System.Threading.Channels;

namespace SpaceSpreadsheetEmulator.Chat.Local;

public sealed class LocalChatDirectory(TimeProvider timeProvider)
{
    private const int MaximumRememberedMessages = 1_024;
    private readonly Dictionary<int, ChannelState> channels = [];
    private readonly object sync = new();

    public LocalChatMembershipResult Join(int solarSystemId, LocalChatMember member)
    {
        ValidateSolarSystemId(solarSystemId);
        ValidateMember(member);
        lock (sync)
        {
            ChannelState channel = GetOrCreate(solarSystemId);
            var key = new MemberKey(member.GatewayId, member.GatewaySessionId);
            if (channel.Members.TryGetValue(key, out LocalChatMember? existing))
            {
                if (existing != member)
                {
                    throw new InvalidOperationException(
                        "The Gateway session is already associated with another local-chat member.");
                }

                return new LocalChatMembershipResult(false, channel.Sequence);
            }

            channel.Members.Add(key, member);
            ulong sequence = checked(++channel.Sequence);
            Publish(channel, new LocalChatMemberJoined(sequence, member));
            return new LocalChatMembershipResult(true, sequence);
        }
    }

    public LocalChatMembershipResult Leave(int solarSystemId, LocalChatMember member)
    {
        ValidateSolarSystemId(solarSystemId);
        ValidateMember(member);
        lock (sync)
        {
            ChannelState channel = GetOrCreate(solarSystemId);
            var key = new MemberKey(member.GatewayId, member.GatewaySessionId);
            if (!channel.Members.Remove(key, out LocalChatMember? existing))
            {
                return new LocalChatMembershipResult(false, channel.Sequence);
            }

            if (existing.CharacterId != member.CharacterId)
            {
                channel.Members.Add(key, existing);
                throw new InvalidOperationException(
                    "The Gateway session belongs to another local-chat member.");
            }

            ulong sequence = checked(++channel.Sequence);
            Publish(channel, new LocalChatMemberLeft(sequence, existing));
            return new LocalChatMembershipResult(true, sequence);
        }
    }

    public LocalChatMessageResult Send(
        int solarSystemId,
        string gatewayId,
        ulong gatewaySessionId,
        string messageId,
        string text)
    {
        ValidateSolarSystemId(solarSystemId);
        ArgumentException.ThrowIfNullOrWhiteSpace(gatewayId);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentOutOfRangeException.ThrowIfZero(gatewaySessionId);
        if (messageId.Length > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(messageId));
        }

        if (text.Length > 1_000)
        {
            throw new ArgumentOutOfRangeException(nameof(text));
        }

        lock (sync)
        {
            ChannelState channel = GetOrCreate(solarSystemId);
            if (!channel.Members.TryGetValue(
                    new MemberKey(gatewayId, gatewaySessionId),
                    out LocalChatMember? sender))
            {
                throw new InvalidOperationException(
                    "Only a current local-chat member may send a message.");
            }

            if (channel.Messages.TryGetValue(messageId, out LocalChatMessage? existing))
            {
                if (existing.CharacterId != sender.CharacterId
                    || !string.Equals(existing.Text, text, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "The local-chat message identifier conflicts with another request.");
                }

                return new LocalChatMessageResult(existing, true);
            }

            var message = new LocalChatMessage(
                messageId,
                sender.CharacterId,
                sender.CharacterName,
                text,
                timeProvider.GetUtcNow());
            channel.Messages.Add(messageId, message);
            channel.MessageOrder.Enqueue(messageId);
            while (channel.MessageOrder.Count > MaximumRememberedMessages)
            {
                channel.Messages.Remove(channel.MessageOrder.Dequeue());
            }

            Publish(channel, new LocalChatMessagePosted(checked(++channel.Sequence), message));
            return new LocalChatMessageResult(message, false);
        }
    }

    public LocalChatSubscription Subscribe(int solarSystemId, int queueCapacity)
    {
        ValidateSolarSystemId(solarSystemId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(queueCapacity);
        lock (sync)
        {
            ChannelState channel = GetOrCreate(solarSystemId);
            Guid subscriptionId = Guid.NewGuid();
            Channel<LocalChatEvent> events = Channel.CreateBounded<LocalChatEvent>(
                new BoundedChannelOptions(queueCapacity)
                {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = true,
                });
            channel.Subscriptions.Add(subscriptionId, events.Writer);
            events.Writer.TryWrite(new LocalChatSnapshot(
                channel.Sequence,
                channel.Members.Values
                    .OrderBy(member => member.CharacterId)
                    .ToArray()));
            return new LocalChatSubscription(
                events.Reader,
                () => RemoveSubscription(solarSystemId, subscriptionId));
        }
    }

    private void RemoveSubscription(int solarSystemId, Guid subscriptionId)
    {
        lock (sync)
        {
            if (channels.TryGetValue(solarSystemId, out ChannelState? channel)
                && channel.Subscriptions.Remove(subscriptionId, out ChannelWriter<LocalChatEvent>? writer))
            {
                writer.TryComplete();
            }
        }
    }

    private static void Publish(ChannelState channel, LocalChatEvent item)
    {
        foreach ((Guid id, ChannelWriter<LocalChatEvent> writer) in
                 channel.Subscriptions.ToArray())
        {
            if (writer.TryWrite(item))
            {
                continue;
            }

            writer.TryComplete(new LocalChatEventGapException());
            channel.Subscriptions.Remove(id);
        }
    }

    private ChannelState GetOrCreate(int solarSystemId)
    {
        if (!channels.TryGetValue(solarSystemId, out ChannelState? channel))
        {
            channel = new ChannelState();
            channels.Add(solarSystemId, channel);
        }

        return channel;
    }

    private static void ValidateSolarSystemId(int solarSystemId)
        => ArgumentOutOfRangeException.ThrowIfNegativeOrZero(solarSystemId);

    private static void ValidateMember(LocalChatMember member)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentException.ThrowIfNullOrWhiteSpace(member.GatewayId);
        ArgumentException.ThrowIfNullOrWhiteSpace(member.CharacterName);
        if (member.GatewaySessionId == 0
            || member.CharacterId <= 0
            || member.CharacterName.Length > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(member));
        }
    }

    private sealed class ChannelState
    {
        public ulong Sequence { get; set; }

        public Dictionary<MemberKey, LocalChatMember> Members { get; } = [];

        public Dictionary<string, LocalChatMessage> Messages { get; } =
            new(StringComparer.Ordinal);

        public Queue<string> MessageOrder { get; } = [];

        public Dictionary<Guid, ChannelWriter<LocalChatEvent>> Subscriptions { get; } = [];
    }

    private readonly record struct MemberKey(string GatewayId, ulong GatewaySessionId);
}
