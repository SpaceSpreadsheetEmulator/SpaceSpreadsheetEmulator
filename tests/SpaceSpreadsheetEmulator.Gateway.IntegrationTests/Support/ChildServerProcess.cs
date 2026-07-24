using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Abstractions;
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
    private readonly IFileSystem fileSystem;
    private readonly IDirectoryInfo runtimeDirectory;

    private ChildServerProcess(
        IFileSystem fileSystem,
        string name,
        Process process,
        IDirectoryInfo runtimeDirectory)
    {
        this.fileSystem = fileSystem;
        Name = name;
        this.process = process;
        this.runtimeDirectory = runtimeDirectory;
        standardOutput = CaptureAsync(process.StandardOutput);
        standardError = CaptureAsync(process.StandardError);
    }

    public string Name { get; }

    public bool HasExited => process.HasExited;

    public static ChildServerProcess Start(
        IFileSystem fileSystem,
        string name,
        string assemblyPath,
        string environment,
        Action<JsonObject> configureSettings)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        if (!fileSystem.File.Exists(assemblyPath))
        {
            throw new FileNotFoundException($"The {name} test assembly has not been built.", assemblyPath);
        }

        string assemblyDirectory = fileSystem.Path.GetDirectoryName(assemblyPath)
            ?? throw new DirectoryNotFoundException($"Could not locate the {name} assembly directory.");
        string baseSettingsSource = fileSystem.Path.Combine(assemblyDirectory, "appsettings.json");
        string profileFileName = $"appsettings.{environment}.json";
        string profileSettingsSource = fileSystem.Path.Combine(assemblyDirectory, profileFileName);
        if (!fileSystem.File.Exists(baseSettingsSource) || !fileSystem.File.Exists(profileSettingsSource))
        {
            throw new FileNotFoundException(
                $"The {name} {environment} appsettings files have not been built.");
        }

        IDirectoryInfo runtimeDirectory = fileSystem.Directory.CreateTempSubdirectory(
            $"space-spreadsheet-emulator-{name.ToLowerInvariant()}-");
        fileSystem.File.Copy(
            baseSettingsSource,
            fileSystem.Path.Combine(runtimeDirectory.FullName, "appsettings.json"));
        JsonObject settings = JsonNode.Parse(fileSystem.File.ReadAllText(baseSettingsSource))?.AsObject()
            ?? throw new InvalidDataException(
                $"The {name} base appsettings file is empty.");
        JsonObject profileSettings = JsonNode.Parse(fileSystem.File.ReadAllText(profileSettingsSource))?.AsObject()
            ?? throw new InvalidDataException(
                $"The {name} {environment} appsettings file is empty.");
        Merge(settings, profileSettings);
        configureSettings(settings);
        fileSystem.File.WriteAllText(
            fileSystem.Path.Combine(runtimeDirectory.FullName, profileFileName),
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
            return new ChildServerProcess(fileSystem, name, process, runtimeDirectory);
        }
        catch
        {
            fileSystem.Directory.Delete(runtimeDirectory.FullName, recursive: true);
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
        if (fileSystem.Directory.Exists(runtimeDirectory.FullName))
        {
            fileSystem.Directory.Delete(runtimeDirectory.FullName, recursive: true);
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
