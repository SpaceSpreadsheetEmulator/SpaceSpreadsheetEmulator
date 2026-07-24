using System.IO.Compression;
using System.IO.Abstractions;
using System.Text;
using SpaceSpreadsheetEmulator.Milestone2.Tests.Support;
using SpaceSpreadsheetEmulator.StaticData;

namespace SpaceSpreadsheetEmulator.Milestone2.Tests.StaticData;

public class StaticDataPromotionTests
{
    private static readonly IFileSystem FileSystem = new FileSystem();
    private static readonly StaticDataPromoter Promoter = new(FileSystem, TimeProvider.System);

    private static readonly (string Name, string Json)[] Entries =
    [
        ("races.jsonl", "{\"_key\":1,\"name\":{\"en\":\"Test Race\"}}"),
        ("bloodlines.jsonl", "{\"_key\":2,\"name\":{\"en\":\"Test Bloodline\"},\"raceID\":1,\"corporationID\":6}"),
        ("ancestries.jsonl", "{\"_key\":3,\"name\":{\"en\":\"Test Ancestry\"},\"bloodlineID\":2}"),
        ("factions.jsonl", "{\"_key\":4,\"name\":{\"en\":\"Test Faction\"},\"corporationID\":6}"),
        ("types.jsonl", "{\"_key\":5,\"name\":{\"en\":\"Test Type\"},\"description\":{\"en\":\"Test ship\"},\"groupID\":50,\"published\":true,\"mass\":1000,\"radius\":12,\"volume\":50,\"capacity\":25,\"portionSize\":1}"),
        ("groups.jsonl", "{\"_key\":50,\"name\":{\"en\":\"Test Group\"},\"categoryID\":6}"),
        ("npcCorporations.jsonl", "{\"_key\":6,\"name\":{\"en\":\"Test Corporation\"},\"stationID\":7}"),
        ("npcCharacters.jsonl", "{\"_key\":11,\"name\":{\"en\":\"Test Agent\"},\"agent\":{\"agentTypeID\":2,\"divisionID\":22,\"level\":3,\"isLocator\":true},\"locationID\":7,\"bloodlineID\":2,\"corporationID\":6,\"gender\":true}"),
        ("npcStations.jsonl", "{\"_key\":7,\"name\":{\"en\":\"Test Station\"},\"solarSystemID\":10,\"typeID\":5,\"ownerID\":6,\"operationID\":26}"),
        ("mapRegions.jsonl", "{\"_key\":8,\"name\":{\"en\":\"Test Region\"},\"factionID\":4}"),
        ("mapConstellations.jsonl", "{\"_key\":9,\"name\":{\"en\":\"Test Constellation\"},\"regionID\":8}"),
        ("mapSolarSystems.jsonl", "{\"_key\":10,\"name\":{\"en\":\"Test System\"},\"constellationID\":9,\"regionID\":8}"),
        ("dogmaAttributes.jsonl", "{\"_key\":37,\"attributeCategoryID\":17,\"dataType\":4,\"defaultValue\":0,\"description\":\"Maximum velocity\",\"displayName\":{\"en\":\"Maximum Velocity\"},\"displayWhenZero\":false,\"highIsGood\":true,\"name\":\"maxVelocity\",\"published\":true,\"stackable\":false,\"unitID\":11}"),
        ("dogmaEffects.jsonl", "{\"_key\":5000,\"disallowAutoRepeat\":false,\"effectCategoryID\":0,\"guid\":\"effects.shipMaxTargetRangeBonusOnline\",\"isAssistance\":false,\"isOffensive\":false,\"isWarpSafe\":true,\"name\":\"shipMaxTargetRangeBonusOnline\",\"published\":false}"),
        ("typeDogma.jsonl", "{\"_key\":5,\"dogmaAttributes\":[{\"attributeID\":37,\"value\":295}],\"dogmaEffects\":[{\"effectID\":5000,\"isDefault\":false}]}"),
    ];

    [Fact]
    public async Task VerifiedArchivePromotesToImmutableHashAddressedArtifact()
    {
        using var temporary = new TemporaryDirectory(FileSystem);
        string archive = FileSystem.Path.Combine(temporary.Path, "mini-sde.zip");
        CreateArchive(archive, build: 3396210);
        string sourceHash = await StaticDataPromoter.ComputeSha256Async(FileSystem, archive);

        string artifact = await Promoter.PromoteAsync(
            archive,
            FileSystem.Path.Combine(temporary.Path, "artifacts"),
            3396210,
            sourceHash);
        await using SqliteStaticDataStore store = await SqliteStaticDataStore.OpenAsync(
            FileSystem,
            artifact);
        StaticDataRecord? system = await store.FindAsync(StaticDataEntityKind.SolarSystem, 10);
        StaticDataRecord? station = await store.FindAsync(StaticDataEntityKind.NpcStation, 7);
        StaticTypeDefinition? type = await store.FindTypeAsync(5);
        DogmaAttributeDefinition attribute = Assert.Single(await store.ListDogmaAttributesAsync());
        DogmaEffectDefinition effect = Assert.Single(await store.ListDogmaEffectsAsync());
        TypeDogmaDefinition? typeDogma = await store.FindTypeDogmaAsync(5);
        StaticNpcAgent agent = Assert.Single(await store.ListNpcAgentsAsync());

        Assert.Equal(3396210, store.Compatibility.ProtocolProfile);
        Assert.Equal("Test System", system!.Name);
        Assert.Equal(9, system.ParentId);
        Assert.Equal(8, system.SecondaryParentId);
        Assert.Equal(26, station!.OperationId);
        Assert.Equal("Test ship", type!.Description);
        Assert.Equal(1000, type.Mass);
        Assert.Equal("maxVelocity", attribute.Name);
        Assert.Equal("shipMaxTargetRangeBonusOnline", effect.Name);
        Assert.Equal(295, Assert.Single(typeDogma!.Attributes).Value);
        Assert.Equal(11, agent.AgentId);
        Assert.Equal(7, agent.StationId);
        Assert.True(agent.IsLocatorAgent);
        Assert.EndsWith(store.Compatibility.ArtifactSha256, artifact, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildMismatchIsRejectedWithoutPublishingArtifact()
    {
        using var temporary = new TemporaryDirectory(FileSystem);
        string archive = FileSystem.Path.Combine(temporary.Path, "wrong-build.zip");
        CreateArchive(archive, build: 3441022);
        string sourceHash = await StaticDataPromoter.ComputeSha256Async(FileSystem, archive);
        string output = FileSystem.Path.Combine(temporary.Path, "artifacts");

        await Assert.ThrowsAsync<InvalidDataException>(() => Promoter.PromoteAsync(
            archive,
            output,
            3396210,
            sourceHash));

        Assert.False(FileSystem.Directory.Exists(FileSystem.Path.Combine(output, "3396210")));
    }

    private static void CreateArchive(string path, int build)
    {
        using Stream archiveStream = FileSystem.File.Create(path);
        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create);
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
