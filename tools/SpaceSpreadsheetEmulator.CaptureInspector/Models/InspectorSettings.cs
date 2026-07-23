namespace SpaceSpreadsheetEmulator.CaptureInspector.Models;

public sealed class InspectorSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public string? CcpStaticDataArchivePath { get; set; }

    public string? ClientExportLocation { get; set; }

    public Dictionary<string, bool> IdentifierResolution { get; set; } = IdentifierFields.DefaultSettings();
}
