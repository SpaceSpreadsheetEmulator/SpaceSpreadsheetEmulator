using System.IO.Abstractions;
using SpaceSpreadsheetEmulator.CaptureInspector.Services;

namespace SpaceSpreadsheetEmulator.CaptureInspector.Tests;

public sealed class InspectorSettingsStoreTests
{
    private static readonly IFileSystem FileSystem = new FileSystem();

    [Fact]
    public void SaveAndLoadRoundTripsPathsAndIdentifierSettings()
    {
        using var directory = TemporaryDirectory.Create(FileSystem);
        var store = new InspectorSettingsStore(FileSystem, directory.Path);
        var settings = store.Load();
        settings.CcpStaticDataArchivePath = "/data/sde.zip";
        settings.ClientExportLocation = "/data/client-export";
        settings.IdentifierResolution["typeID"] = false;

        store.Save(settings);
        var loaded = store.Load();

        Assert.Equal("/data/sde.zip", loaded.CcpStaticDataArchivePath);
        Assert.Equal("/data/client-export", loaded.ClientExportLocation);
        Assert.False(loaded.IdentifierResolution["typeID"]);
        Assert.True(loaded.IdentifierResolution["stationID"]);
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
