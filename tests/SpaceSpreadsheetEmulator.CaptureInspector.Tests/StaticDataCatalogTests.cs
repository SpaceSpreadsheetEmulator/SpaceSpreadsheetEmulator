using System.IO.Compression;
using SpaceSpreadsheetEmulator.CaptureInspector.Services;

namespace SpaceSpreadsheetEmulator.CaptureInspector.Tests;

public sealed class StaticDataCatalogTests
{
    [Fact]
    public async Task LoadArchiveAsyncResolvesSupportedStaticIdentifiers()
    {
        using var directory = TemporaryDirectory.Create();
        string archivePath = CreateArchive(directory.Path);
        await using var catalog = new StaticDataCatalog(System.IO.Path.Combine(directory.Path, "cache"));

        await catalog.LoadArchiveAsync(archivePath);

        Assert.Equal("Tritanium", await catalog.ResolveAsync("typeID", 34));
        Assert.Equal("Jita IV - Moon 4", await catalog.ResolveAsync("stationID", 60003760));
        Assert.Null(await catalog.ResolveAsync("itemID", 34));
        Assert.Equal(3396210, catalog.Compatibility!.SdeBuild);
    }

    private static string CreateArchive(string directory)
    {
        string archivePath = System.IO.Path.Combine(directory, "sde.jsonl.zip");
        using var stream = File.Create(archivePath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        WriteEntry(archive, "_sde.jsonl", """{"buildNumber":3396210,"releaseDate":"2026-07-24T00:00:00Z"}""");
        WriteEntry(archive, "races.jsonl", """{"_key":1,"name":"Race"}""");
        WriteEntry(archive, "bloodlines.jsonl", """{"_key":2,"name":"Bloodline"}""");
        WriteEntry(archive, "ancestries.jsonl", """{"_key":3,"name":"Ancestry"}""");
        WriteEntry(archive, "factions.jsonl", """{"_key":500001,"name":"Faction"}""");
        WriteEntry(archive, "types.jsonl", """{"_key":34,"name":{"en":"Tritanium"}}""");
        WriteEntry(archive, "npcCorporations.jsonl", """{"_key":1000035,"name":"Corporation"}""");
        WriteEntry(archive, "npcStations.jsonl", """{"_key":60003760,"name":"Jita IV - Moon 4"}""");
        WriteEntry(archive, "mapRegions.jsonl", """{"_key":10000002,"name":"The Forge"}""");
        WriteEntry(archive, "mapConstellations.jsonl", """{"_key":20000020,"name":"Kimotoro"}""");
        WriteEntry(archive, "mapSolarSystems.jsonl", """{"_key":30000142,"name":"Jita"}""");
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
        private TemporaryDirectory(string path) => Path = path;

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
