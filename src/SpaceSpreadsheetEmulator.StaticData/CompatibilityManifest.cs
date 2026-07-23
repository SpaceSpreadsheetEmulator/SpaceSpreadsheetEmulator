namespace SpaceSpreadsheetEmulator.StaticData;

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
