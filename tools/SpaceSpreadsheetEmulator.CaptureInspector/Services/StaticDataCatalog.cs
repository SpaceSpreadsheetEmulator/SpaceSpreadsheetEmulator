using System.IO.Compression;
using System.IO.Abstractions;
using System.Globalization;
using System.Text.Json;
using SpaceSpreadsheetEmulator.CaptureInspector.Models;
using SpaceSpreadsheetEmulator.StaticData;

namespace SpaceSpreadsheetEmulator.CaptureInspector.Services;

public sealed class StaticDataCatalog : IIdentifierResolver, IAsyncDisposable
{
    private static readonly IReadOnlyDictionary<string, StaticDataEntityKind> FieldKinds =
        new Dictionary<string, StaticDataEntityKind>(StringComparer.Ordinal)
        {
            ["typeID"] = StaticDataEntityKind.Type,
            ["stationID"] = StaticDataEntityKind.NpcStation,
            ["solarSystemID"] = StaticDataEntityKind.SolarSystem,
            ["constellationID"] = StaticDataEntityKind.Constellation,
            ["regionID"] = StaticDataEntityKind.Region,
            ["corporationID"] = StaticDataEntityKind.NpcCorporation,
            ["factionID"] = StaticDataEntityKind.Faction,
            ["raceID"] = StaticDataEntityKind.Race,
            ["bloodlineID"] = StaticDataEntityKind.Bloodline,
            ["ancestryID"] = StaticDataEntityKind.Ancestry,
        };

    private readonly string cacheDirectory;
    private readonly IFileSystem fileSystem;
    private readonly StaticDataPromoter promoter;
    private readonly Dictionary<(StaticDataEntityKind Kind, long Id), string?> lookupCache = [];
    private SqliteStaticDataStore? store;

    public StaticDataCatalog(
        IFileSystem fileSystem,
        string cacheDirectory,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        ArgumentNullException.ThrowIfNull(timeProvider);
        this.fileSystem = fileSystem;
        this.cacheDirectory = cacheDirectory;
        promoter = new StaticDataPromoter(fileSystem, timeProvider);
    }

    public CompatibilityManifest? Compatibility => store?.Compatibility;

    public async Task LoadArchiveAsync(string archivePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        int build = await ReadBuildAsync(archivePath, cancellationToken);
        string sourceHash = await StaticDataPromoter.ComputeSha256Async(
            fileSystem,
            archivePath,
            cancellationToken);
        string? artifactDirectory = await FindCachedArtifactAsync(build, sourceHash, cancellationToken);
        artifactDirectory ??= await promoter.PromoteAsync(
            archivePath,
            cacheDirectory,
            build,
            sourceHash,
            cancellationToken);

        SqliteStaticDataStore nextStore = await SqliteStaticDataStore.OpenAsync(
            fileSystem,
            artifactDirectory,
            cancellationToken);
        SqliteStaticDataStore? previousStore = Interlocked.Exchange(ref store, nextStore);
        lookupCache.Clear();
        if (previousStore is not null)
        {
            await previousStore.DisposeAsync();
        }
    }

    public async ValueTask<string?> ResolveAsync(string fieldName, long identifier, CancellationToken cancellationToken = default)
    {
        if (!FieldKinds.TryGetValue(fieldName, out StaticDataEntityKind kind) || store is null)
        {
            return null;
        }

        if (lookupCache.TryGetValue((kind, identifier), out string? cached))
        {
            return cached;
        }

        StaticDataRecord? record = await store.FindAsync(kind, identifier, cancellationToken);
        string? name = string.IsNullOrWhiteSpace(record?.Name) ? null : record.Name;
        lookupCache[(kind, identifier)] = name;
        return name;
    }

    public async Task ClearAsync()
    {
        SqliteStaticDataStore? previousStore = Interlocked.Exchange(ref store, null);
        lookupCache.Clear();
        if (previousStore is not null)
        {
            await previousStore.DisposeAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await ClearAsync();
    }

    private async Task<int> ReadBuildAsync(string archivePath, CancellationToken cancellationToken)
    {
        await using Stream stream = fileSystem.File.OpenRead(archivePath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        ZipArchiveEntry entry = archive.GetEntry("_sde.jsonl")
            ?? throw new InvalidDataException("The selected archive has no _sde.jsonl metadata entry.");
        await using Stream metadata = entry.Open();
        using JsonDocument document = await JsonDocument.ParseAsync(metadata, cancellationToken: cancellationToken);
        return document.RootElement.GetProperty("buildNumber").GetInt32();
    }

    private async Task<string?> FindCachedArtifactAsync(int build, string sourceHash, CancellationToken cancellationToken)
    {
        string buildDirectory = fileSystem.Path.Combine(cacheDirectory, build.ToString(CultureInfo.InvariantCulture));
        if (!fileSystem.Directory.Exists(buildDirectory))
        {
            return null;
        }

        foreach (string candidate in fileSystem.Directory.EnumerateDirectories(buildDirectory))
        {
            string manifestPath = fileSystem.Path.Combine(candidate, StaticDataPromoter.ManifestFileName);
            if (!fileSystem.File.Exists(manifestPath))
            {
                continue;
            }

            await using Stream manifest = fileSystem.File.OpenRead(manifestPath);
            CompatibilityManifest? compatibility = await JsonSerializer.DeserializeAsync<CompatibilityManifest>(manifest, cancellationToken: cancellationToken);
            if (compatibility is not null && string.Equals(compatibility.SourceSha256, sourceHash, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }
}
