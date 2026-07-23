using System.IO.Compression;
using System.Text;
using SpaceSpreadsheetEmulator.Milestone2.Tests.Support;
using SpaceSpreadsheetEmulator.StaticData;

namespace SpaceSpreadsheetEmulator.Milestone2.Tests.StaticData;

public class StaticDataPromotionTests
{
    private static readonly (string Name, string Json)[] Entries =
    [
        ("races.jsonl", "{\"_key\":1,\"name\":{\"en\":\"Test Race\"}}"),
        ("bloodlines.jsonl", "{\"_key\":2,\"name\":{\"en\":\"Test Bloodline\"},\"raceID\":1,\"corporationID\":6}"),
        ("ancestries.jsonl", "{\"_key\":3,\"name\":{\"en\":\"Test Ancestry\"},\"bloodlineID\":2}"),
        ("factions.jsonl", "{\"_key\":4,\"name\":{\"en\":\"Test Faction\"},\"corporationID\":6}"),
        ("types.jsonl", "{\"_key\":5,\"name\":{\"en\":\"Test Type\"},\"groupID\":50}"),
        ("npcCorporations.jsonl", "{\"_key\":6,\"name\":{\"en\":\"Test Corporation\"},\"stationID\":7}"),
        ("npcStations.jsonl", "{\"_key\":7,\"name\":{\"en\":\"Test Station\"},\"solarSystemID\":10,\"typeID\":5,\"ownerID\":6}"),
        ("mapRegions.jsonl", "{\"_key\":8,\"name\":{\"en\":\"Test Region\"},\"factionID\":4}"),
        ("mapConstellations.jsonl", "{\"_key\":9,\"name\":{\"en\":\"Test Constellation\"},\"regionID\":8}"),
        ("mapSolarSystems.jsonl", "{\"_key\":10,\"name\":{\"en\":\"Test System\"},\"constellationID\":9,\"regionID\":8}"),
    ];

    [Fact]
    public async Task VerifiedArchivePromotesToImmutableHashAddressedArtifact()
    {
        using var temporary = new TemporaryDirectory();
        string archive = Path.Combine(temporary.Path, "mini-sde.zip");
        CreateArchive(archive, build: 3396210);
        string sourceHash = await StaticDataPromoter.ComputeSha256Async(archive);

        string artifact = await StaticDataPromoter.PromoteAsync(
            archive,
            Path.Combine(temporary.Path, "artifacts"),
            3396210,
            sourceHash);
        await using SqliteStaticDataStore store = await SqliteStaticDataStore.OpenAsync(artifact);
        StaticDataRecord? system = await store.FindAsync(StaticDataEntityKind.SolarSystem, 10);

        Assert.Equal(3396210, store.Compatibility.ProtocolProfile);
        Assert.Equal("Test System", system!.Name);
        Assert.Equal(9, system.ParentId);
        Assert.Equal(8, system.SecondaryParentId);
        Assert.EndsWith(store.Compatibility.ArtifactSha256, artifact, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildMismatchIsRejectedWithoutPublishingArtifact()
    {
        using var temporary = new TemporaryDirectory();
        string archive = Path.Combine(temporary.Path, "wrong-build.zip");
        CreateArchive(archive, build: 3441022);
        string sourceHash = await StaticDataPromoter.ComputeSha256Async(archive);
        string output = Path.Combine(temporary.Path, "artifacts");

        await Assert.ThrowsAsync<InvalidDataException>(() => StaticDataPromoter.PromoteAsync(
            archive,
            output,
            3396210,
            sourceHash));

        Assert.False(Directory.Exists(Path.Combine(output, "3396210")));
    }

    private static void CreateArchive(string path, int build)
    {
        using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteEntry(archive, "_sde.jsonl", $"{{\"buildNumber\":{build},\"releaseDate\":\"2026-06-16T00:00:00Z\"}}");
        foreach ((string name, string json) in Entries)
        {
            WriteEntry(archive, name, json);
        }
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.SmallestSize);
        using Stream stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.WriteLine(content);
    }
}
