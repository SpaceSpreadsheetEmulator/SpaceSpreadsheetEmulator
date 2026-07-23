using System.Buffers;
using System.Globalization;
using SpaceSpreadsheetEmulator.Protocol.Codec;
using SpaceSpreadsheetEmulator.Protocol.Profiles;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Protocol.Tests.Fixtures;

public class FixtureRoundTripTests
{
    [Fact]
    public void EverySuccessfulBlueFixturePreservesExactBytes()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "fixtures");
        string[] fixtures = Directory.GetFiles(root, "*.hex", SearchOption.AllDirectories)
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

    private static bool IsSuccessfulBlueFixture(string hexPath)
    {
        string json = File.ReadAllText(Path.ChangeExtension(hexPath, ".json"));
        return json.Contains("\"codec\": \"blue\"", StringComparison.Ordinal)
            && json.Contains("\"success\": true", StringComparison.Ordinal);
    }

    private static byte[] ReadHex(string path)
    {
        var bytes = new List<byte>();
        foreach (string line in File.ReadLines(path))
        {
            foreach (string token in line.Split('#')[0].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                bytes.Add(byte.Parse(token, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture));
            }
        }

        return [.. bytes];
    }
}
