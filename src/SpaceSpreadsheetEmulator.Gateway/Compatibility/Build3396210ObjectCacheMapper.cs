using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using SpaceSpreadsheetEmulator.Protocol.Codec;
using SpaceSpreadsheetEmulator.Protocol.Compression;
using SpaceSpreadsheetEmulator.Protocol.Profiles;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Gateway.Compatibility;

/// <summary>
/// Builds the two-step method-cache exchange observed for build-3396210 startup data.
/// </summary>
internal static class Build3396210ObjectCacheMapper
{
    private const string MethodResultType =
        "carbon.common.script.net.objectCaching.CachedMethodCallResult";
    private const string CachedObjectType =
        "carbon.common.script.net.cachedObject.CachedObject";
    private const string CachePayloadType =
        "carbon.common.script.net.objectCaching.CachedObject";

    public static Build3396210ObjectCacheEntry CreateEntry(
        string service,
        string method,
        PyValue value,
        long nodeId,
        DateTimeOffset createdAt,
        ProtocolProfile profile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(service);
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentNullException.ThrowIfNull(value);

        byte[] marshal = BlueMarshalCodec.Encode(value, profile);
        byte[] compressed = new ZlibPayloadCodec(profile.Limits).Compress(marshal);
        byte[] digest = SHA256.HashData(marshal);
        int versionTag = BitConverter.ToInt32(digest, 0) & int.MaxValue;
        var version = new PyTuple(
            new PyBigInteger(createdAt.UtcDateTime.ToFileTimeUtc()),
            new PyInteger(versionTag));
        var key = new PyTuple(
            Bytes("Method Call"),
            new PyText("server"),
            new PyTuple(Bytes(service), Bytes(method)));
        return new Build3396210ObjectCacheEntry(
            service,
            method,
            key,
            version,
            nodeId,
            ImmutableArray.Create(compressed));
    }

    public static PyObject CreateMethodResult(Build3396210ObjectCacheEntry entry)
        => new(
            Bytes(MethodResultType),
            new PyTuple(
                new PyDictionary(new PyDictionaryEntry(
                    Bytes("versionCheck"),
                    Bytes("run"))),
                new PyObject(
                    Bytes(CachedObjectType),
                    new PyTuple(entry.Key, new PyInteger(entry.NodeId), entry.Version)),
                PyNull.Instance));

    public static PyObject CreateCachePayload(Build3396210ObjectCacheEntry entry)
        => new(
            Bytes(CachePayloadType),
            new PyTuple(
                entry.Version,
                PyNull.Instance,
                new PyInteger(entry.NodeId),
                new PyInteger(1),
                new PyBuffer(entry.CompressedPayload),
                new PyInteger(1),
                entry.Key));

    private static PyBuffer Bytes(string value)
        => new(Encoding.UTF8.GetBytes(value));
}

internal sealed record Build3396210ObjectCacheEntry(
    string Service,
    string Method,
    PyTuple Key,
    PyTuple Version,
    long NodeId,
    ImmutableArray<byte> CompressedPayload);
