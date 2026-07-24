using System.IO.Abstractions;

namespace SpaceSpreadsheetEmulator.CaptureInspector.Services;

public sealed record InspectorApplicationPaths(string ConfigurationDirectory, string CacheDirectory)
{
    public static InspectorApplicationPaths CreateDefault(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        string configurationRoot = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string cacheRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new InspectorApplicationPaths(
            fileSystem.Path.Combine(configurationRoot, "SpaceSpreadsheetEmulator", "CaptureInspector"),
            fileSystem.Path.Combine(cacheRoot, "SpaceSpreadsheetEmulator", "CaptureInspector", "static-data"));
    }
}
