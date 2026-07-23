using System.Buffers;
using SpaceSpreadsheetEmulator.Protocol.Profiles;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Protocol.Codec;

public static class BlueMarshalCodec
{
    public static DecodeResult<PyValue> Decode(
        ReadOnlySequence<byte> input,
        ProtocolProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (input.Length > profile.Limits.MaximumDecompressedBytes)
        {
            return DecodeResult<PyValue>.Failure(new ProtocolError(
                ProtocolErrorCodes.LimitExceeded,
                0,
                "$",
                $"The marshal document exceeds {profile.Limits.MaximumDecompressedBytes} bytes."));
        }

        try
        {
            var decoder = new BlueMarshalDecoder(input, profile);
            PyValue value = decoder.ReadRoot();
            return DecodeResult<PyValue>.Success(value);
        }
        catch (ProtocolReadException exception)
        {
            return DecodeResult<PyValue>.Failure(exception.Error);
        }
    }

    public static void Encode(
        PyValue value,
        IBufferWriter<byte> output,
        ProtocolProfile profile,
        EncodingMode mode = EncodingMode.Canonical)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(profile);

        var encoder = new BlueMarshalEncoder(output, profile, mode);
        encoder.Write(value);
    }

    public static byte[] Encode(
        PyValue value,
        ProtocolProfile profile,
        EncodingMode mode = EncodingMode.Canonical)
    {
        var output = new ArrayBufferWriter<byte>();
        Encode(value, output, profile, mode);
        return output.WrittenSpan.ToArray();
    }
}
