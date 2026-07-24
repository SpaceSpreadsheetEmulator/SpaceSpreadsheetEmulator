using System.Globalization;
using System.IO.Abstractions;

namespace SpaceSpreadsheetEmulator.Protocol.Tool;

/// <summary>
/// Parses and formats the human-readable hexadecimal files used by protocol tooling.
/// </summary>
internal static class HexFiles
{
    public static byte[] Read(IFileSystem fileSystem, string path)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        return Parse(fileSystem.File.ReadAllText(path));
    }

    public static byte[] Parse(string text)
    {
        var bytes = new List<byte>();
        foreach (string originalLine in text.Split('\n'))
        {
            string line = originalLine.Split('#', 2)[0];
            foreach (string token in line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                bytes.Add(byte.Parse(token, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture));
            }
        }

        return [.. bytes];
    }

    public static string Format(ReadOnlySpan<byte> bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
}
