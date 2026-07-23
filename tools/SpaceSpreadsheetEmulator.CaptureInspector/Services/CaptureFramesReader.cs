using System.Text.Json;
using SpaceSpreadsheetEmulator.CaptureInspector.Models;

namespace SpaceSpreadsheetEmulator.CaptureInspector.Services;

public static class CaptureFramesReader
{
    public const int SupportedDecoderSchemaVersion = 2;

    public static async Task<CaptureLoadResult> ReadAsync(string path, int maximumFrames, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumFrames);

        var frames = new List<CaptureFrame>();
        var diagnostics = new List<string>();
        await using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream);
        var lineNumber = 0;

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(line);
                JsonElement root = document.RootElement;
                if (!root.TryGetProperty("decoder_schema_version", out JsonElement schema) ||
                    schema.ValueKind != JsonValueKind.Number ||
                    schema.GetInt32() != SupportedDecoderSchemaVersion)
                {
                    diagnostics.Add($"Line {lineNumber}: unsupported or missing decoder schema version.");
                    continue;
                }

                frames.Add(CreateFrame(root));
                if (frames.Count == maximumFrames)
                {
                    return new CaptureLoadResult(frames, diagnostics, IsTruncated: true);
                }
            }
            catch (JsonException)
            {
                diagnostics.Add($"Line {lineNumber}: invalid JSON.");
            }
        }

        if (frames.Count == 0)
        {
            throw new InvalidDataException("The file contains no supported decoder schema-v2 frame records.");
        }

        return new CaptureLoadResult(frames, diagnostics, IsTruncated: false);
    }

    private static CaptureFrame CreateFrame(JsonElement root) => new(
        ReadInt32(root, "frame_index") ?? 0,
        ReadString(root, "direction") ?? "unknown",
        ReadDouble(root, "start_relative_ms") ?? ReadDouble(root, "end_relative_ms"),
        ReadInt32(root, "stream_frame_index"),
        ReadInt32(root, "wire_size"),
        ReadString(root, "message_type") ?? "—",
        ReadString(root, "service") ?? string.Empty,
        ReadString(root, "method") ?? ReadString(root, "notification") ?? string.Empty,
        ReadInt64(root, "call_id"),
        ReadString(root, "decode_status") ?? "—",
        ReadString(root, "record_kind") ?? "protocol_frame",
        ReadString(root, "decoded_kind") ?? string.Empty,
        ReadString(root, "compression") ?? string.Empty,
        ReadString(root, "raw_base64"),
        root.TryGetProperty("decoded_payload", out JsonElement payload) ? payload.Clone() : null,
        ReadString(root, "frame_index_kind") ?? string.Empty,
        ReadString(root, "capture_layer") ?? string.Empty,
        ReadString(root, "payload_sha256") ?? string.Empty,
        ReadInt64(root, "start_sequence"),
        ReadInt64(root, "end_sequence"),
        ReadDouble(root, "end_relative_ms"));

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? ReadInt32(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out int number) ? number : null;

    private static long? ReadInt64(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt64(out long number) ? number : null;

    private static double? ReadDouble(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetDouble(out double number) ? number : null;
}
