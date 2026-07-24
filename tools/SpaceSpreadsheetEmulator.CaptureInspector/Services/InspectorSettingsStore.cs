using System.Text.Json;
using System.IO.Abstractions;
using SpaceSpreadsheetEmulator.CaptureInspector.Models;

namespace SpaceSpreadsheetEmulator.CaptureInspector.Services;

public sealed class InspectorSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly IFileSystem fileSystem;
    private readonly string settingsPath;

    public InspectorSettingsStore(IFileSystem fileSystem, string configurationDirectory)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationDirectory);
        this.fileSystem = fileSystem;
        settingsPath = fileSystem.Path.Combine(configurationDirectory, "settings.json");
    }

    public string SettingsPath => settingsPath;

    public InspectorSettings Load()
    {
        try
        {
            if (!fileSystem.File.Exists(settingsPath))
            {
                return new InspectorSettings();
            }

            InspectorSettings? settings = JsonSerializer.Deserialize<InspectorSettings>(
                fileSystem.File.ReadAllText(settingsPath));
            return settings is { SchemaVersion: InspectorSettings.CurrentSchemaVersion }
                ? Normalize(settings)
                : new InspectorSettings();
        }
        catch (IOException)
        {
            return new InspectorSettings();
        }
        catch (JsonException)
        {
            return new InspectorSettings();
        }
    }

    public void Save(InspectorSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        fileSystem.Directory.CreateDirectory(fileSystem.Path.GetDirectoryName(settingsPath)!);
        string temporaryPath = $"{settingsPath}.{Guid.NewGuid():N}.tmp";
        fileSystem.File.WriteAllText(temporaryPath, JsonSerializer.Serialize(Normalize(settings), SerializerOptions));
        fileSystem.File.Move(temporaryPath, settingsPath, overwrite: true);
    }

    private static InspectorSettings Normalize(InspectorSettings settings)
    {
        settings.IdentifierResolution ??= IdentifierFields.DefaultSettings();
        foreach (string field in IdentifierFields.Supported)
        {
            settings.IdentifierResolution.TryAdd(field, true);
        }

        return settings;
    }
}
