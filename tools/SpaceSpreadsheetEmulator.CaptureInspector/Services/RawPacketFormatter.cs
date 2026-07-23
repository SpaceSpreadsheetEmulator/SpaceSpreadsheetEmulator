using System.Globalization;

namespace SpaceSpreadsheetEmulator.CaptureInspector.Services;

public static class RawPacketFormatter
{
    public static string Format(string? rawBase64)
    {
        if (string.IsNullOrWhiteSpace(rawBase64))
        {
            return "This frame export does not include raw_base64 bytes.";
        }

        byte[] bytes = Convert.FromBase64String(rawBase64);
        const int bytesPerLine = 16;
        var lines = new List<string>((bytes.Length + bytesPerLine - 1) / bytesPerLine);
        for (var offset = 0; offset < bytes.Length; offset += bytesPerLine)
        {
            ReadOnlySpan<byte> line = bytes.AsSpan(offset, Math.Min(bytesPerLine, bytes.Length - offset));
            string hex = string.Join(' ', line.ToArray().Select(static value => value.ToString("X2", CultureInfo.InvariantCulture)));
            string ascii = new(line.ToArray().Select(static value => value is >= 32 and <= 126 ? (char)value : '.').ToArray());
            lines.Add($"{offset:X8}  {hex,-47}  {ascii}");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
