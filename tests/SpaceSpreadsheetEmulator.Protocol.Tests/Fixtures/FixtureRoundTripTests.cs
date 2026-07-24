using System.Buffers;
using System.Globalization;
using System.IO.Abstractions;
using SpaceSpreadsheetEmulator.Protocol.Codec;
using SpaceSpreadsheetEmulator.Protocol.Profiles;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Protocol.Tests.Fixtures;

public class FixtureRoundTripTests
{
    private readonly IFileSystem fileSystem = new FileSystem();

    [Fact]
    public void EverySuccessfulBlueFixturePreservesExactBytes()
    {
        string root = fileSystem.Path.Combine(AppContext.BaseDirectory, "fixtures");
        string[] fixtures = fileSystem.Directory.GetFiles(root, "*.hex", SearchOption.AllDirectories)
            .Where(path => IsSuccessfulBlueFixture(path))
            .ToArray();
        ProtocolProfile profile = ProtocolProfileCatalog.GetRequired(3_396_210);

        Assert.Equal(44, fixtures.Length);
        foreach (string fixture in fixtures)
        {
            byte[] bytes = ReadHex(fixture);
            DecodeResult<PyValue> decoded = BlueMarshalCodec.Decode(new ReadOnlySequence<byte>(bytes), profile);
            Assert.True(decoded.IsSuccess, fixture);
            Assert.Equal(bytes, BlueMarshalCodec.Encode(decoded.Value!, profile, EncodingMode.PreserveWireForm));
        }
    }

    private bool IsSuccessfulBlueFixture(string hexPath)
    {
        string json = fileSystem.File.ReadAllText(fileSystem.Path.ChangeExtension(hexPath, ".json"));
        return json.Contains("\"codec\": \"blue\"", StringComparison.Ordinal)
            && json.Contains("\"success\": true", StringComparison.Ordinal);
    }

    private byte[] ReadHex(string path)
    {
        var bytes = new List<byte>();
        foreach (string line in fileSystem.File.ReadLines(path))
        {
            foreach (string token in line.Split('#')[0].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                bytes.Add(byte.Parse(token, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture));
            }
        }

        return [.. bytes];
    }
}
