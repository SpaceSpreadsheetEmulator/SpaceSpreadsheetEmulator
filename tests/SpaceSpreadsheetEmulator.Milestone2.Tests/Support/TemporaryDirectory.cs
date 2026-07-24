using System.IO.Abstractions;

namespace SpaceSpreadsheetEmulator.Milestone2.Tests.Support;

internal sealed class TemporaryDirectory : IDisposable
{
    private readonly IFileSystem fileSystem;

    public TemporaryDirectory(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        this.fileSystem = fileSystem;
        Path = fileSystem.Path.Combine(
            fileSystem.Path.GetTempPath(),
            $"sse-m2-{Guid.NewGuid():N}");
        fileSystem.Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (fileSystem.Directory.Exists(Path))
        {
            fileSystem.Directory.Delete(Path, recursive: true);
        }
    }
}
