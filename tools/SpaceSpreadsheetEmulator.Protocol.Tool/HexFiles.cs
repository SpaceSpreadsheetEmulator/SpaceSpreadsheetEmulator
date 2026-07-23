using System.Globalization;

namespace SpaceSpreadsheetEmulator.Protocol.Tool;

internal static class HexFiles
{
    public static byte[] Read(string path) => Parse(File.ReadAllText(path));

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
