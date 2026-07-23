using System.Buffers;
using System.Text.Json.Nodes;
using SpaceSpreadsheetEmulator.Protocol.Codec;
using SpaceSpreadsheetEmulator.Protocol.Compression;
using SpaceSpreadsheetEmulator.Protocol.Framing;
using SpaceSpreadsheetEmulator.Protocol.MachoNet;
using SpaceSpreadsheetEmulator.Protocol.Profiles;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Protocol.Tool;

/// <summary>
/// Verifies cleared protocol fixtures against their expected decode and round-trip behavior.
/// </summary>
internal static class FixtureVerifier
{
    public static int Verify(string root)
    {
        ProtocolProfile profile = ProtocolProfileCatalog.GetRequired(ProtocolProfileCatalog.SupportedBuild);
        int failures = 0;
        string[] hexFiles = Directory.GetFiles(root, "*.hex", SearchOption.AllDirectories);
        foreach (string hexPath in hexFiles.Order(StringComparer.Ordinal))
        {
            string jsonPath = Path.ChangeExtension(hexPath, ".json");
            try
            {
                VerifyOne(hexPath, jsonPath, profile);
                Console.WriteLine($"PASS {Path.GetRelativePath(root, hexPath)}");
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine($"FAIL {Path.GetRelativePath(root, hexPath)}: {exception.Message}");
            }
        }

        Console.WriteLine($"Verified {hexFiles.Length - failures}/{hexFiles.Length} fixtures.");
        return failures == 0 ? 0 : 1;
    }

    private static void VerifyOne(string hexPath, string jsonPath, ProtocolProfile profile)
    {
        byte[] bytes = HexFiles.Read(hexPath);
        JsonObject expected = JsonNode.Parse(File.ReadAllText(jsonPath))!.AsObject();
        string codec = expected["codec"]?.GetValue<string>() ?? "blue";
        bool success = expected["success"]?.GetValue<bool>() ?? true;

        switch (codec)
        {
            case "blue":
                VerifyBlue(bytes, expected, success, profile);
                break;
            case "frame":
                VerifyFrame(bytes, expected, success);
                break;
            case "compression":
                VerifyCompression(bytes, expected, success);
                break;
            case "macho":
                VerifyMacho(bytes, expected, success, profile);
                break;
            default:
                throw new InvalidDataException($"Unknown fixture codec '{codec}'.");
        }
    }

    private static void VerifyBlue(byte[] bytes, JsonObject expected, bool success, ProtocolProfile profile)
    {
        DecodeResult<PyValue> result = BlueMarshalCodec.Decode(new ReadOnlySequence<byte>(bytes), profile);
        Require(result.IsSuccess == success, result.Error?.Message ?? "Unexpected success.");
        if (!success)
        {
            Require(result.Error?.Code == expected["errorCode"]?.GetValue<string>(), "The error code did not match.");
            if (expected["offset"] is JsonNode offset)
            {
                Require(result.Error?.ByteOffset == offset.GetValue<long>(), "The error offset did not match.");
            }

            if (expected["path"] is JsonNode path)
            {
                Require(result.Error?.ValuePath == path.GetValue<string>(), "The error path did not match.");
            }

            return;
        }

        byte[] preserved = BlueMarshalCodec.Encode(result.Value!, profile, EncodingMode.PreserveWireForm);
        Require(bytes.AsSpan().SequenceEqual(preserved), "PreserveWireForm did not reproduce the fixture.");
        string expectedKind = expected["kind"]?.GetValue<string>() ?? throw new InvalidDataException("Expected kind is missing.");
        JsonNode actual = ValueJson.ToNode(result.Value!);
        string actualKind = actual["kind"]!.GetValue<string>();
        Require(actualKind == expectedKind, $"Expected kind {expectedKind}, got {actualKind}.");
        if (expected["semantic"] is JsonNode expectedSemantic)
        {
            Require(JsonNode.DeepEquals(actual, expectedSemantic), "The decoded semantic value did not match.");
        }
    }

    private static void VerifyFrame(byte[] bytes, JsonObject expected, bool success)
    {
        var sequenceReader = new SequenceReader<byte>(new ReadOnlySequence<byte>(bytes));
        FrameReadStatus status = new FrameReader().TryReadFrame(ref sequenceReader, out ReadOnlySequence<byte> payload, out _);
        Require((status == FrameReadStatus.Complete) == success, $"Unexpected frame status {status}.");
        if (success)
        {
            Require(HexFiles.Format(payload.ToArray()) == expected["payloadHex"]?.GetValue<string>(), "Frame payload differs.");
        }
    }

    private static void VerifyCompression(byte[] bytes, JsonObject expected, bool success)
    {
        DecodeResult<BinaryPayload> result = new ZlibPayloadCodec().Decompress(bytes);
        Require(result.IsSuccess == success, result.Error?.Message ?? "Unexpected compression success.");
        if (success)
        {
            Require(HexFiles.Format(result.Value!.Bytes) == expected["payloadHex"]?.GetValue<string>(), "Decompressed payload differs.");
        }
    }

    private static void VerifyMacho(byte[] bytes, JsonObject expected, bool success, ProtocolProfile profile)
    {
        DecodeResult<MachoPacket> result = MachoPacketCodec.Decode(new ReadOnlySequence<byte>(bytes), profile);
        Require(result.IsSuccess == success, result.Error?.Message ?? "Unexpected MachoNet success.");
        if (success)
        {
            Require(result.Value!.NumericType == expected["numericType"]?.GetValue<int>(), "Packet type differs.");
            byte[] canonical = MachoPacketCodec.Encode(result.Value, profile);
            Require(canonical.Length > 0, "Packet re-encoding was empty.");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }
}
