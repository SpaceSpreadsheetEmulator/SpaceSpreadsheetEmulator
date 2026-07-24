namespace SpaceSpreadsheetEmulator.Chat.Local;

public sealed record LocalChatMember(
    string GatewayId,
    ulong GatewaySessionId,
    long CharacterId,
    string CharacterName);

public sealed record LocalChatMessage(
    string MessageId,
    long CharacterId,
    string CharacterName,
    string Text,
    DateTimeOffset SentAt);

public abstract record LocalChatEvent(ulong Sequence);

public sealed record LocalChatSnapshot(
    ulong Sequence,
    IReadOnlyList<LocalChatMember> Members) : LocalChatEvent(Sequence);

public sealed record LocalChatMemberJoined(
    ulong Sequence,
    LocalChatMember Member) : LocalChatEvent(Sequence);

public sealed record LocalChatMemberLeft(
    ulong Sequence,
    LocalChatMember Member) : LocalChatEvent(Sequence);

public sealed record LocalChatMessagePosted(
    ulong Sequence,
    LocalChatMessage Message) : LocalChatEvent(Sequence);

public sealed record LocalChatMembershipResult(bool Changed, ulong Sequence);

public sealed record LocalChatMessageResult(
    LocalChatMessage Message,
    bool AlreadyApplied);

public sealed class LocalChatEventGapException : InvalidOperationException
{
    public LocalChatEventGapException()
        : base("The local-chat subscriber fell behind its bounded queue.")
    {
    }
}
