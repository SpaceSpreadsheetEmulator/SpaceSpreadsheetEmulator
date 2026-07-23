using System.Buffers;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SpaceSpreadsheetEmulator.Protocol;
using SpaceSpreadsheetEmulator.Protocol.Codec;
using SpaceSpreadsheetEmulator.Protocol.Profiles;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Gateway.Compatibility;

/// <summary>
/// Loads a bounded, hash-verified allowlist of local startup responses for compatibility testing.
/// </summary>
internal sealed class CapturedStartupReplay
{
    private const string ManifestFileName = "manifest.json";
    private const int MaximumResponses = 128;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };
    private readonly ImmutableDictionary<string, ImmutableArray<CapturedReplayResponse>> responses;

    private CapturedStartupReplay(
        ImmutableDictionary<string, ImmutableArray<CapturedReplayResponse>> responses)
    {
        this.responses = responses;
    }

    public static CapturedStartupReplay? LoadOptional(
        string directory,
        ProtocolProfile profile,
        Func<string, bool> isRouteAllowed)
    {
        ArgumentNullException.ThrowIfNull(isRouteAllowed);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        string root = Path.GetFullPath(directory);
        string manifestPath = Path.Combine(root, ManifestFileName);
        ReplayManifest manifest = JsonSerializer.Deserialize<ReplayManifest>(
            File.ReadAllText(manifestPath),
            JsonOptions) ?? throw new InvalidDataException("The captured startup manifest is empty.");
        ValidateManifest(manifest, profile);

        var loaded = ImmutableArray.CreateBuilder<CapturedReplayResponse>(manifest.Entries.Length);
        foreach (ReplayManifestEntry entry in manifest.Entries)
        {
            if (!isRouteAllowed(entry.Route))
            {
                continue;
            }

            string path = ResolveFile(root, entry.File);
            byte[] marshal = File.ReadAllBytes(path);
            string actualHash = Convert.ToHexStringLower(SHA256.HashData(marshal));
            if (!string.Equals(actualHash, entry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Captured startup response hash mismatch: {entry.File}");
            }

            DecodeResult<PyValue> decoded = BlueMarshalCodec.Decode(
                new ReadOnlySequence<byte>(marshal),
                profile);
            if (!decoded.IsSuccess)
            {
                throw new InvalidDataException(
                    $"Captured startup response {entry.File} is invalid for client build {profile.Build}: {decoded.Error}");
            }

            loaded.Add(new CapturedReplayResponse(entry.Route, entry.Match, decoded.Value!));
        }

        return new CapturedStartupReplay(loaded
            .GroupBy(response => response.Route, StringComparer.Ordinal)
            .ToImmutableDictionary(
                group => group.Key,
                group => group.ToImmutableArray(),
                StringComparer.Ordinal));
    }

    public bool TryGet(string route, out ImmutableArray<CapturedReplayResponse> routeResponses)
        => responses.TryGetValue(route, out routeResponses);

    private static void ValidateManifest(ReplayManifest manifest, ProtocolProfile profile)
    {
        if (manifest.FormatVersion != 1)
        {
            throw new InvalidDataException("The captured startup manifest version is unsupported.");
        }

        if (manifest.TargetClientBuild != profile.Build)
        {
            throw new InvalidDataException(
                $"Captured startup data targets build {manifest.TargetClientBuild}, not {profile.Build}.");
        }

        if (manifest.Entries.Length is 0 or > MaximumResponses
            || manifest.ResponseCount != manifest.Entries.Length)
        {
            throw new InvalidDataException("The captured startup response count is invalid.");
        }

        foreach (ReplayManifestEntry entry in manifest.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Route)
                || string.IsNullOrWhiteSpace(entry.File)
                || string.IsNullOrWhiteSpace(entry.Sha256))
            {
                throw new InvalidDataException("A captured startup manifest entry is incomplete.");
            }
        }
    }

    private static string ResolveFile(string root, string fileName)
    {
        if (!string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Captured startup response paths must be simple file names.");
        }

        return Path.Combine(root, fileName);
    }

    private sealed record ReplayManifest(
        int FormatVersion,
        int TargetClientBuild,
        int ResponseCount,
        ImmutableArray<ReplayManifestEntry> Entries);

    private sealed record ReplayManifestEntry(
        string Route,
        string File,
        string Sha256,
        string? Match = null);
}

/// <summary>
/// Consumes replay responses in manifest order while matching the current RPC arguments.
/// </summary>
internal sealed class CapturedStartupReplayCursor(CapturedStartupReplay? replay)
{
    private readonly Dictionary<string, int> nextIndexes = new(StringComparer.Ordinal);

    public bool TryTake(string route, PyTuple arguments, out PyValue? value)
    {
        value = null;
        if (replay is null || !replay.TryGet(route, out ImmutableArray<CapturedReplayResponse> responses))
        {
            return false;
        }

        string? match = CreateMatch(route, arguments);
        if (match is not null)
        {
            CapturedReplayResponse? matched = responses.FirstOrDefault(
                response => string.Equals(response.Match, match, StringComparison.Ordinal));
            if (matched is null)
            {
                return false;
            }

            value = matched.Value;
            return true;
        }

        ImmutableArray<CapturedReplayResponse> unkeyed = responses
            .Where(response => response.Match is null)
            .ToImmutableArray();
        if (unkeyed.Length == 0)
        {
            return false;
        }

        if (unkeyed.Length == 1)
        {
            value = unkeyed[0].Value;
            return true;
        }

        int next = nextIndexes.GetValueOrDefault(route);
        if (next >= unkeyed.Length)
        {
            return false;
        }

        nextIndexes[route] = next + 1;
        value = unkeyed[next].Value;
        return true;
    }

    private static string? CreateMatch(string route, PyTuple arguments)
    {
        if (!string.Equals(route, "objectCaching.GetCachableObject", StringComparison.Ordinal)
            || arguments.Items.Length < 2)
        {
            return null;
        }

        PyValue key = Unwrap(arguments.Items[1]);
        string? name = ReadText(key);
        if (name is not null)
        {
            return $"name:{name}";
        }

        if (key is not PyTuple { Items.Length: >= 3 } keyTuple
            || Unwrap(keyTuple.Items[2]) is not PyTuple { Items.Length: >= 2 } methodRoute
            || ReadText(methodRoute.Items[0]) is not string service
            || ReadText(methodRoute.Items[1]) is not string method)
        {
            return null;
        }

        return $"method:{service}.{method}";
    }

    private static PyValue Unwrap(PyValue value)
        => value is PySavedValueReference reference ? reference.Value : value;

    private static string? ReadText(PyValue value)
        => Unwrap(value) switch
        {
            PyText text => text.Value,
            PyToken token => token.Value,
            PyStringTableReference table => table.Value,
            PyBuffer buffer => Encoding.UTF8.GetString(buffer.Value.AsSpan()),
            _ => null,
        };
}

/// <summary>
/// Associates one allowlisted startup route and optional argument match with its decoded response.
/// </summary>
internal sealed record CapturedReplayResponse(string Route, string? Match, PyValue Value);
