using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;

namespace SpaceSpreadsheetEmulator.Protocol.Profiles;

/// <summary>
/// Resolves explicitly supported client builds to their pinned protocol profiles.
/// </summary>
public static class ProtocolProfileCatalog
{
    public const int SupportedBuild = 3_396_210;

    private static readonly Lazy<ProtocolProfile> Build3396210 = new(LoadBuild3396210);

    public static ProtocolProfile GetRequired(int build) => build switch
    {
        SupportedBuild => Build3396210.Value,
        _ => throw new KeyNotFoundException($"Protocol build {build} is not supported."),
    };

    private static ProtocolProfile LoadBuild3396210()
    {
        Assembly assembly = typeof(ProtocolProfileCatalog).Assembly;
        string resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith("Profiles.string-table.tsv", StringComparison.Ordinal));

        using Stream stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("The protocol string table resource is missing.");
        using StreamReader reader = new(stream);

        var entries = ImmutableArray.CreateBuilder<string>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            string[] parts = line.Split('\t', 2);
            if (parts.Length != 2
                || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int index)
                || index != entries.Count)
            {
                throw new InvalidDataException($"Invalid string-table row: {line}");
            }

            entries.Add(parts[1]);
        }

        return new ProtocolProfile(SupportedBuild, entries.ToImmutable(), new ProtocolLimits())
        {
            Handshake = new HandshakeProfile(
                170_472,
                496,
                24.01,
                SupportedBuild,
                "V24.01@ccp"),
        };
    }
}
