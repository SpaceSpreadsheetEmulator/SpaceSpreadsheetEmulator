using System.Security.Cryptography;
using System.Text.Json;
using SpaceSpreadsheetEmulator.Gateway.Compatibility;
using SpaceSpreadsheetEmulator.Protocol.Codec;
using SpaceSpreadsheetEmulator.Protocol.Profiles;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Gateway.IntegrationTests.Compatibility;

public sealed class CapturedStartupReplayTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("name:config.BulkData.races")]
    public void DuplicateRequestKeysAreRejected(string? match)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"sse-replay-keys-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            ProtocolProfile profile = ProtocolProfileCatalog.GetRequired(3_396_210);
            byte[] value = BlueMarshalCodec.Encode(new PyList(), profile);
            string hash = Convert.ToHexStringLower(SHA256.HashData(value));
            File.WriteAllBytes(Path.Combine(directory, "first.marshal"), value);
            File.WriteAllBytes(Path.Combine(directory, "second.marshal"), value);
            string manifest = JsonSerializer.Serialize(new
            {
                formatVersion = 1,
                targetClientBuild = 3_396_210,
                responseCount = 2,
                entries = new[]
                {
                    new
                    {
                        route = "objectCaching.GetCachableObject",
                        file = "first.marshal",
                        sha256 = hash,
                        match,
                    },
                    new
                    {
                        route = "objectCaching.GetCachableObject",
                        file = "second.marshal",
                        sha256 = hash,
                        match,
                    },
                },
            });
            File.WriteAllText(Path.Combine(directory, "manifest.json"), manifest);

            InvalidDataException error = Assert.Throws<InvalidDataException>(
                () => _ = new Build3396210StartupProfile(new GatewayCompatibilityOptions
                {
                    CapturedStartupDataDirectory = directory,
                }));

            Assert.Contains(
                match is null ? "unkeyed" : "duplicate request key",
                error.Message,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
