using System.IO.Abstractions;
using System.Text.Json;

namespace SpaceSpreadsheetEmulator.Protocol.Tests.Support;

internal static class LocalCaptureCorpus
{
    public static bool HasExplicitRoot(IFileSystem fileSystem)
        => !string.IsNullOrWhiteSpace(LoadExplicitRoot(fileSystem));

    public static string ConfiguredRoot(IFileSystem fileSystem)
    {
        string? explicitRoot = LoadExplicitRoot(fileSystem);
        string repositoryRoot = FindRepositoryRoot(fileSystem);
        return !string.IsNullOrWhiteSpace(explicitRoot)
            ? fileSystem.Path.GetFullPath(explicitRoot, repositoryRoot)
            : fileSystem.Path.Combine(repositoryRoot, "_local", "protocol-captures");
    }

    public static bool HasFrameExports(IFileSystem fileSystem)
    {
        string configuredRoot = ConfiguredRoot(fileSystem);
        return fileSystem.Directory.Exists(configuredRoot)
            && fileSystem.Directory.EnumerateFiles(
                configuredRoot,
                "frames*.jsonl",
                SearchOption.AllDirectories).Any();
    }

    public static IReadOnlyList<string> GetFrameExportsOrSkip(IFileSystem fileSystem)
    {
        string[] files = fileSystem.Directory.GetFiles(
            ConfiguredRoot(fileSystem),
            "frames*.jsonl",
            SearchOption.AllDirectories);
        if (files.Length == 0)
        {
            throw new InvalidOperationException("The local protocol corpus contains no frames*.jsonl parser exports.");
        }

        return files;
    }

    public static IEnumerable<LocalMarshalFrame> ReadMarshalFrames(
        IFileSystem fileSystem,
        IEnumerable<string> files)
    {
        foreach (string file in files)
        {
            int lineNumber = 0;
            foreach (string line in fileSystem.File.ReadLines(file))
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

    private static string FindRepositoryRoot(IFileSystem fileSystem)
    {
        IDirectoryInfo? directory = fileSystem.DirectoryInfo.New(AppContext.BaseDirectory);
        while (directory is not null && !fileSystem.File.Exists(fileSystem.Path.Combine(
                   directory.FullName,
                   "SpaceSpreadsheetEmulator.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? fileSystem.Directory.GetCurrentDirectory();
    }

    private static string? LoadExplicitRoot(IFileSystem fileSystem)
    {
        string localSettings = fileSystem.Path.Combine(
            AppContext.BaseDirectory,
            "appsettings.UnitTest.local.json");
        string settingsPath = fileSystem.File.Exists(localSettings)
            ? localSettings
            : fileSystem.Path.Combine(AppContext.BaseDirectory, "appsettings.UnitTest.json");
        using JsonDocument document = JsonDocument.Parse(fileSystem.File.ReadAllText(settingsPath));
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
        if (!LocalCaptureCorpus.HasFrameExports(new FileSystem()))
        {
            Skip = "No local frames*.jsonl corpus is configured.";
        }
    }
}

public sealed class ExplicitLocalCaptureFactAttribute : FactAttribute
{
    public ExplicitLocalCaptureFactAttribute()
    {
        IFileSystem fileSystem = new FileSystem();
        if (!LocalCaptureCorpus.HasExplicitRoot(fileSystem))
        {
            Skip = "Set ProtocolTests:LocalCaptureDirectory in appsettings.UnitTest.local.json.";
        }
        else if (!LocalCaptureCorpus.HasFrameExports(fileSystem))
        {
            Skip = "The configured local capture directory contains no frames*.jsonl exports.";
        }
    }
}
