using System.IO.Compression;
using System.IO.Abstractions;
using SpaceSpreadsheetEmulator.CaptureInspector.Services;

namespace SpaceSpreadsheetEmulator.CaptureInspector.Tests;

public sealed class StaticDataCatalogTests
{
    private static readonly IFileSystem FileSystem = new FileSystem();

    [Fact]
    public async Task LoadArchiveAsyncResolvesSupportedStaticIdentifiers()
    {
        using var directory = TemporaryDirectory.Create(FileSystem);
        string archivePath = CreateArchive(FileSystem, directory.Path);
        await using var catalog = new StaticDataCatalog(
            FileSystem,
            FileSystem.Path.Combine(directory.Path, "cache"),
            TimeProvider.System);

        await catalog.LoadArchiveAsync(archivePath);

        Assert.Equal("Tritanium", await catalog.ResolveAsync("typeID", 34));
        Assert.Equal("Jita IV - Moon 4", await catalog.ResolveAsync("stationID", 60003760));
        Assert.Null(await catalog.ResolveAsync("itemID", 34));
        Assert.Equal(3396210, catalog.Compatibility!.SdeBuild);
    }

    private static string CreateArchive(IFileSystem fileSystem, string directory)
    {
        string archivePath = fileSystem.Path.Combine(directory, "sde.jsonl.zip");
        using Stream stream = fileSystem.File.Create(archivePath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        WriteEntry(archive, "_sde.jsonl", """{"buildNumber":3396210,"releaseDate":"2026-07-24T00:00:00Z"}""");
        WriteEntry(archive, "races.jsonl", """{"_key":1,"name":"Race"}""");
        WriteEntry(archive, "bloodlines.jsonl", """{"_key":2,"name":"Bloodline"}""");
        WriteEntry(archive, "ancestries.jsonl", """{"_key":3,"name":"Ancestry"}""");
        WriteEntry(archive, "factions.jsonl", """{"_key":500001,"name":"Faction"}""");
        WriteEntry(archive, "types.jsonl", """{"_key":34,"name":{"en":"Tritanium"},"groupID":18}""");
        WriteEntry(archive, "groups.jsonl", """{"_key":18,"name":{"en":"Mineral"},"categoryID":4}""");
        WriteEntry(archive, "npcCorporations.jsonl", """{"_key":1000035,"name":"Corporation"}""");
        WriteEntry(
            archive,
            "npcCharacters.jsonl",
            """{"_key":3000001,"name":{"en":"Agent"},"agent":{"agentTypeID":2,"divisionID":22,"level":1,"isLocator":false},"locationID":60003760,"bloodlineID":2,"corporationID":1000035,"gender":false}""");
        WriteEntry(archive, "npcStations.jsonl", """{"_key":60003760,"name":"Jita IV - Moon 4"}""");
        WriteEntry(archive, "mapRegions.jsonl", """{"_key":10000002,"name":"The Forge"}""");
        WriteEntry(archive, "mapConstellations.jsonl", """{"_key":20000020,"name":"Kimotoro"}""");
        WriteEntry(archive, "mapSolarSystems.jsonl", """{"_key":30000142,"name":"Jita"}""");
        WriteEntry(archive, "dogmaAttributes.jsonl", """{"_key":37,"dataType":4,"defaultValue":0,"name":"maxVelocity","published":true,"stackable":false}""");
        WriteEntry(archive, "dogmaEffects.jsonl", """{"_key":5000,"effectCategoryID":0,"guid":"effects.shipMaxTargetRangeBonusOnline","name":"shipMaxTargetRangeBonusOnline"}""");
        WriteEntry(archive, "typeDogma.jsonl", """{"_key":34,"dogmaAttributes":[],"dogmaEffects":[]}""");
        return archivePath;
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name);
        using StreamWriter writer = new(entry.Open());
        writer.Write(content);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly IFileSystem fileSystem;

        private TemporaryDirectory(IFileSystem fileSystem, string path)
        {
            this.fileSystem = fileSystem;
            Path = path;
        }

        public string Path { get; }

        public static TemporaryDirectory Create(IFileSystem fileSystem)
        {
            string path = fileSystem.Path.Combine(
                fileSystem.Path.GetTempPath(),
                Guid.NewGuid().ToString("N"));
            fileSystem.Directory.CreateDirectory(path);
            return new TemporaryDirectory(fileSystem, path);
        }

        public void Dispose() => fileSystem.Directory.Delete(Path, recursive: true);
    }
}
