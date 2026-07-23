using System.Collections.Immutable;

namespace SpaceSpreadsheetEmulator.Protocol.Profiles;

/// <summary>
/// Groups the string table, safety limits, and handshake constants for one supported client build.
/// </summary>
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

/// <summary>
/// Defines the build-specific values exchanged while negotiating a client connection.
/// </summary>
public sealed record HandshakeProfile(
    int EveBirthday,
    int MachoVersion,
    double ClientVersion,
    int ClientBuild,
    string ProjectVersion)
{
    public static HandshakeProfile Unspecified { get; } = new(0, 0, 0, 0, string.Empty);
}
