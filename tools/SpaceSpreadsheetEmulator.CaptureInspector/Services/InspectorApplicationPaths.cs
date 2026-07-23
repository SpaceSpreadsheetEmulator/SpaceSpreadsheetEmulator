namespace SpaceSpreadsheetEmulator.CaptureInspector.Services;

public sealed record InspectorApplicationPaths(string ConfigurationDirectory, string CacheDirectory)
{
    public static InspectorApplicationPaths CreateDefault()
    {
        string configurationRoot = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string cacheRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new InspectorApplicationPaths(
            Path.Combine(configurationRoot, "SpaceSpreadsheetEmulator", "CaptureInspector"),
            Path.Combine(cacheRoot, "SpaceSpreadsheetEmulator", "CaptureInspector", "static-data"));
    }
}
