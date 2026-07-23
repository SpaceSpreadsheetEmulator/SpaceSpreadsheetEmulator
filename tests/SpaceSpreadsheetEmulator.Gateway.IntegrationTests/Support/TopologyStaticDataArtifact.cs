using System.IO.Compression;
using System.Text;
using SpaceSpreadsheetEmulator.StaticData;

namespace SpaceSpreadsheetEmulator.Gateway.IntegrationTests.Support;

internal sealed class TopologyStaticDataArtifact : IAsyncDisposable
{
    private TopologyStaticDataArtifact(string root, string artifactDirectory)
    {
        Root = root;
        ArtifactDirectory = artifactDirectory;
    }

    public string Root { get; }

    public string ArtifactDirectory { get; }

    public static async Task<TopologyStaticDataArtifact> CreateAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), $"sse-topology-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string archivePath = Path.Combine(root, "sde.zip");
        using (ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            Write(archive, "_sde.jsonl", "{\"buildNumber\":3396210,\"releaseDate\":\"2026-06-16T00:00:00Z\"}");
            Write(archive, "races.jsonl", "{\"_key\":1,\"name\":{\"en\":\"Caldari\"}}");
            Write(archive, "bloodlines.jsonl", "{\"_key\":1,\"name\":{\"en\":\"Deteis\"},\"raceID\":1,\"corporationID\":1000002}");
            Write(archive, "ancestries.jsonl", "{\"_key\":10,\"name\":{\"en\":\"Scientist\"},\"bloodlineID\":1}");
            Write(archive, "factions.jsonl", "{\"_key\":500001,\"name\":{\"en\":\"Caldari State\"},\"corporationID\":1000002}");
            Write(archive, "types.jsonl", "{\"_key\":1373,\"name\":{\"en\":\"Caldari Achura\"},\"groupID\":1}\n{\"_key\":601,\"name\":{\"en\":\"Ibis\"},\"groupID\":25}\n{\"_key\":1531,\"name\":{\"en\":\"Station\"},\"groupID\":15}");
            Write(archive, "groups.jsonl", "{\"_key\":1,\"name\":{\"en\":\"Character\"},\"categoryID\":1}\n{\"_key\":15,\"name\":{\"en\":\"Station\"},\"categoryID\":3}\n{\"_key\":25,\"name\":{\"en\":\"Frigate\"},\"categoryID\":6}");
            Write(archive, "npcCorporations.jsonl", "{\"_key\":1000002,\"name\":{\"en\":\"Caldari Provisions\"},\"stationID\":60000004}");
            Write(archive, "npcCharacters.jsonl", "{\"_key\":3008416,\"name\":{\"en\":\"Test Agent\"},\"agent\":{\"agentTypeID\":2,\"divisionID\":22,\"level\":1,\"isLocator\":false},\"locationID\":60000004,\"bloodlineID\":1,\"corporationID\":1000002,\"gender\":false}");
            Write(archive, "npcStations.jsonl", "{\"_key\":60000004,\"name\":{\"en\":\"New Caldari Prime\"},\"solarSystemID\":30002780,\"typeID\":1531,\"ownerID\":1000002,\"operationID\":26}");
            Write(archive, "mapRegions.jsonl", "{\"_key\":10000033,\"name\":{\"en\":\"The Forge\"},\"factionID\":500001}");
            Write(archive, "mapConstellations.jsonl", "{\"_key\":20000407,\"name\":{\"en\":\"Kimotoro\"},\"regionID\":10000033}");
            Write(archive, "mapSolarSystems.jsonl", "{\"_key\":30002780,\"name\":{\"en\":\"New Caldari\"},\"constellationID\":20000407,\"regionID\":10000033}");
        }

        string hash = await StaticDataPromoter.ComputeSha256Async(archivePath);
        string artifact = await StaticDataPromoter.PromoteAsync(
            archivePath,
            Path.Combine(root, "artifacts"),
            3_396_210,
            hash);
        return new TopologyStaticDataArtifact(root, artifact);
    }

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }

        return ValueTask.CompletedTask;
    }

    private static void Write(ZipArchive archive, string name, string content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.SmallestSize);
        using Stream stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.WriteLine(content);
    }
}
