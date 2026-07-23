using System.Text.Json;

namespace SpaceSpreadsheetEmulator.Protocol.Tests.Support;

internal static class LocalCaptureCorpus
{
    private static readonly string? ExplicitRoot = LoadExplicitRoot();

    public static bool HasExplicitRoot => !string.IsNullOrWhiteSpace(ExplicitRoot);

    public static string ConfiguredRoot => HasExplicitRoot
        ? Path.GetFullPath(ExplicitRoot!, FindRepositoryRoot())
        : Path.Combine(FindRepositoryRoot(), "_local", "protocol-captures");

    public static bool HasFrameExports()
        => Directory.Exists(ConfiguredRoot)
            && Directory.EnumerateFiles(ConfiguredRoot, "frames*.jsonl", SearchOption.AllDirectories).Any();

    public static IReadOnlyList<string> GetFrameExportsOrSkip()
    {
        string[] files = Directory.GetFiles(ConfiguredRoot, "frames*.jsonl", SearchOption.AllDirectories);
        if (files.Length == 0)
        {
            throw new InvalidOperationException("The local protocol corpus contains no frames*.jsonl parser exports.");
        }

        return files;
    }

    public static IEnumerable<LocalMarshalFrame> ReadMarshalFrames(IEnumerable<string> files)
    {
        foreach (string file in files)
        {
            int lineNumber = 0;
            foreach (string line in File.ReadLines(file))
            {
                lineNumber++;
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                using JsonDocument document = JsonDocument.Parse(line);
                JsonElement root = document.RootElement;
                if (!TryGetString(root, "decode_status", out string? status)
                    || status != "marshal_decoded"
                    || !TryGetString(root, "raw_base64", out string? rawBase64))
                {
                    continue;
                }

                string compression = TryGetString(root, "compression", out string? value) ? value! : "none";
                bool isMachoPacket = TryGetString(root, "decoded_kind", out string? kind) && kind == "macho_packet";
                int? messageTypeCode = TryGetInt32(root, "message_type_code", out int typeCode) ? typeCode : null;
                string? objectName = TryGetString(root, "object_name", out string? name) ? name : null;
                string? sourceType = TryGetNestedString(root, "source", "type");
                string? destinationType = TryGetNestedString(root, "destination", "type");
                yield return new LocalMarshalFrame(
                    lineNumber,
                    compression,
                    isMachoPacket,
                    messageTypeCode,
                    objectName,
                    sourceType,
                    destinationType,
                    Convert.FromBase64String(rawBase64!));
            }
        }
    }

    private static bool TryGetString(JsonElement value, string property, out string? result)
    {
        if (value.TryGetProperty(property, out JsonElement child) && child.ValueKind == JsonValueKind.String)
        {
            result = child.GetString();
            return true;
        }

        result = null;
        return false;
    }

    private static bool TryGetInt32(JsonElement value, string property, out int result)
    {
        if (value.TryGetProperty(property, out JsonElement child)
            && child.ValueKind == JsonValueKind.Number
            && child.TryGetInt32(out result))
        {
            return true;
        }

        result = 0;
        return false;
    }

    private static string? TryGetNestedString(JsonElement value, string property, string childProperty)
        => value.TryGetProperty(property, out JsonElement child)
            && child.ValueKind == JsonValueKind.Object
            && TryGetString(child, childProperty, out string? result)
                ? result
                : null;

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SpaceSpreadsheetEmulator.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? Directory.GetCurrentDirectory();
    }

    private static string? LoadExplicitRoot()
    {
        string localSettings = Path.Combine(
            AppContext.BaseDirectory,
            "appsettings.UnitTest.local.json");
        string settingsPath = File.Exists(localSettings)
            ? localSettings
            : Path.Combine(AppContext.BaseDirectory, "appsettings.UnitTest.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(settingsPath));
        return document.RootElement
            .GetProperty("ProtocolTests")
            .GetProperty("LocalCaptureDirectory")
            .GetString();
    }
}

internal sealed record LocalMarshalFrame(
    int RecordNumber,
    string Compression,
    bool IsMachoPacket,
    int? MessageTypeCode,
    string? ObjectName,
    string? SourceType,
    string? DestinationType,
    byte[] Payload);

public sealed class LocalCaptureFactAttribute : FactAttribute
{
    public LocalCaptureFactAttribute()
    {
        if (!LocalCaptureCorpus.HasFrameExports())
        {
            Skip = "No local frames*.jsonl corpus is configured.";
        }
    }
}

public sealed class ExplicitLocalCaptureFactAttribute : FactAttribute
{
    public ExplicitLocalCaptureFactAttribute()
    {
        if (!LocalCaptureCorpus.HasExplicitRoot)
        {
            Skip = "Set ProtocolTests:LocalCaptureDirectory in appsettings.UnitTest.local.json.";
        }
        else if (!LocalCaptureCorpus.HasFrameExports())
        {
            Skip = "The configured local capture directory contains no frames*.jsonl exports.";
        }
    }
}
