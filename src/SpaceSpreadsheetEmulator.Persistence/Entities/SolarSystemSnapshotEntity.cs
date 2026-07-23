namespace SpaceSpreadsheetEmulator.Persistence.Entities;

internal sealed class SolarSystemSnapshotEntity
{
    public int SolarSystemId { get; set; }

    public long SourceEpoch { get; set; }

    public int FormatVersion { get; set; }

    public long Tick { get; set; }

    public long LastSequence { get; set; }

    public required byte[] Payload { get; set; }

    public required byte[] PayloadSha256 { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public long Version { get; set; }
}
