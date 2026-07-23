using System.Collections.Immutable;

namespace SpaceSpreadsheetEmulator.Protocol.Profiles;

public sealed record ProtocolProfile(
    int Build,
    ImmutableArray<string> StringTable,
    ProtocolLimits Limits)
{
    public HandshakeProfile Handshake { get; init; } = HandshakeProfile.Unspecified;

    public string GetString(int index)
    {
        if ((uint)index >= (uint)StringTable.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return StringTable[index];
    }
}

public sealed record HandshakeProfile(
    int EveBirthday,
    int MachoVersion,
    double ClientVersion,
    int ClientBuild,
    string ProjectVersion)
{
    public static HandshakeProfile Unspecified { get; } = new(0, 0, 0, 0, string.Empty);
}
