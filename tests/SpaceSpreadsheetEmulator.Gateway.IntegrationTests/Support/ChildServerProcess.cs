using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SpaceSpreadsheetEmulator.Gateway.IntegrationTests.Support;

internal sealed class ChildServerProcess : IAsyncDisposable
{
    private const int MaximumLogLines = 200;
    private readonly Process process;
    private readonly ConcurrentQueue<string> logLines = new();
    private readonly Task standardOutput;
    private readonly Task standardError;
    private readonly DirectoryInfo runtimeDirectory;

    private ChildServerProcess(string name, Process process, DirectoryInfo runtimeDirectory)
    {
        Name = name;
        this.process = process;
        this.runtimeDirectory = runtimeDirectory;
        standardOutput = CaptureAsync(process.StandardOutput);
        standardError = CaptureAsync(process.StandardError);
    }

    public string Name { get; }

    public bool HasExited => process.HasExited;

    public static ChildServerProcess Start(
        string name,
        string assemblyPath,
        string environment,
        Action<JsonObject> configureSettings)
    {
        if (!File.Exists(assemblyPath))
        {
            throw new FileNotFoundException($"The {name} test assembly has not been built.", assemblyPath);
        }

        string assemblyDirectory = Path.GetDirectoryName(assemblyPath)
            ?? throw new DirectoryNotFoundException($"Could not locate the {name} assembly directory.");
        string baseSettingsSource = Path.Combine(assemblyDirectory, "appsettings.json");
        string profileFileName = $"appsettings.{environment}.json";
        string profileSettingsSource = Path.Combine(assemblyDirectory, profileFileName);
        if (!File.Exists(baseSettingsSource) || !File.Exists(profileSettingsSource))
        {
            throw new FileNotFoundException(
                $"The {name} {environment} appsettings files have not been built.");
        }

        DirectoryInfo runtimeDirectory = Directory.CreateTempSubdirectory(
            $"space-spreadsheet-emulator-{name.ToLowerInvariant()}-");
        File.Copy(baseSettingsSource, Path.Combine(runtimeDirectory.FullName, "appsettings.json"));
        JsonObject settings = JsonNode.Parse(File.ReadAllText(baseSettingsSource))?.AsObject()
            ?? throw new InvalidDataException(
                $"The {name} base appsettings file is empty.");
        JsonObject profileSettings = JsonNode.Parse(File.ReadAllText(profileSettingsSource))?.AsObject()
            ?? throw new InvalidDataException(
                $"The {name} {environment} appsettings file is empty.");
        Merge(settings, profileSettings);
        configureSettings(settings);
        File.WriteAllText(
            Path.Combine(runtimeDirectory.FullName, profileFileName),
            settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var startInfo = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = runtimeDirectory.FullName,
        };
        startInfo.ArgumentList.Add(assemblyPath);
        startInfo.ArgumentList.Add("--environment");
        startInfo.ArgumentList.Add(environment);

        try
        {
            Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"Could not start the {name} test process.");
            return new ChildServerProcess(name, process, runtimeDirectory);
        }
        catch
        {
            runtimeDirectory.Delete(recursive: true);
            throw;
        }
    }

    public string DescribeLogs()
        => string.Join(Environment.NewLine, logLines);

    public async ValueTask DisposeAsync()
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }

        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException)
        {
        }

        await Task.WhenAll(standardOutput, standardError).WaitAsync(TimeSpan.FromSeconds(5));
        process.Dispose();
        if (runtimeDirectory.Exists)
        {
            runtimeDirectory.Delete(recursive: true);
        }
    }

    private async Task CaptureAsync(StreamReader reader)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            logLines.Enqueue(line);
            while (logLines.Count > MaximumLogLines)
            {
                logLines.TryDequeue(out _);
            }
        }
    }

    private static void Merge(JsonObject target, JsonObject source)
    {
        foreach ((string propertyName, JsonNode? sourceValue) in source)
        {
            if (target[propertyName] is JsonObject targetObject
                && sourceValue is JsonObject sourceObject)
            {
                Merge(targetObject, sourceObject);
                continue;
            }

            target[propertyName] = sourceValue?.DeepClone();
        }
    }
}
