using System.Collections.Concurrent;
using System.Diagnostics;

namespace SpaceSpreadsheetEmulator.Gateway.IntegrationTests.Support;

internal sealed class ChildServerProcess : IAsyncDisposable
{
    private const int MaximumLogLines = 200;
    private readonly Process process;
    private readonly ConcurrentQueue<string> logLines = new();
    private readonly Task standardOutput;
    private readonly Task standardError;

    private ChildServerProcess(string name, Process process)
    {
        Name = name;
        this.process = process;
        standardOutput = CaptureAsync(process.StandardOutput);
        standardError = CaptureAsync(process.StandardError);
    }

    public string Name { get; }

    public bool HasExited => process.HasExited;

    public static ChildServerProcess Start(
        string name,
        string assemblyPath,
        IReadOnlyDictionary<string, string> environment)
    {
        if (!File.Exists(assemblyPath))
        {
            throw new FileNotFoundException($"The {name} test assembly has not been built.", assemblyPath);
        }

        var startInfo = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(assemblyPath)
                ?? throw new DirectoryNotFoundException($"Could not locate the {name} assembly directory."),
        };
        startInfo.ArgumentList.Add(assemblyPath);
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Testing";
        startInfo.Environment["DOTNET_ENVIRONMENT"] = "Testing";
        foreach ((string key, string value) in environment)
        {
            startInfo.Environment[key] = value;
        }

        Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start the {name} test process.");
        return new ChildServerProcess(name, process);
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
}
