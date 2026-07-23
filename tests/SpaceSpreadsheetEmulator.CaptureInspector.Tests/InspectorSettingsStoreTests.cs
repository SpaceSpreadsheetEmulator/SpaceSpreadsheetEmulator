using SpaceSpreadsheetEmulator.CaptureInspector.Services;

namespace SpaceSpreadsheetEmulator.CaptureInspector.Tests;

public sealed class InspectorSettingsStoreTests
{
    [Fact]
    public void SaveAndLoadRoundTripsPathsAndIdentifierSettings()
    {
        using var directory = TemporaryDirectory.Create();
        var store = new InspectorSettingsStore(directory.Path);
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
