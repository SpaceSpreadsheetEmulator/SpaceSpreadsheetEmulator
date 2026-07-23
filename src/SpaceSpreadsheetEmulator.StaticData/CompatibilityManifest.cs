namespace SpaceSpreadsheetEmulator.StaticData;

/// <summary>
/// Pins an immutable static-data artifact to its client, protocol, SDE, importer, and content hashes.
/// </summary>
public sealed record CompatibilityManifest(
    int ClientBuild,
    int ProtocolProfile,
    int SdeBuild,
    string SourceVariant,
    string SourceSha256,
    string ImporterVersion,
    int SchemaVersion,
    string ArtifactSha256,
    DateTimeOffset ImportedAt,
    DateTimeOffset SourceReleasedAt);
