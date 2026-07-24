using System.Buffers;
using System.IO.Abstractions;
using SpaceSpreadsheetEmulator.Protocol.Codec;
using SpaceSpreadsheetEmulator.Protocol.Profiles;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Protocol.Tool;

/// <summary>
/// Dispatches protocol decode, encode, comparison, and fixture-verification commands.
/// </summary>
public static class CommandLine
{
    public static int Run(
        string[] arguments,
        TextWriter output,
        TextWriter error,
        IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        try
        {
            if (arguments is ["fixtures", "verify", ..])
            {
                string root = arguments.Length >= 3 ? arguments[2] : FindFixtureRoot(fileSystem);
                return FixtureVerifier.Verify(fileSystem, root);
            }

            ProtocolProfile profile = ProtocolProfileCatalog.GetRequired(ReadProfile(arguments));
            if (arguments is ["decode", var decodeInput, ..])
            {
                DecodeResult<PyValue> result = BlueMarshalCodec.Decode(
                    new ReadOnlySequence<byte>(HexFiles.Read(fileSystem, decodeInput)), profile);
                if (!result.IsSuccess)
                {
                    error.WriteLine(System.Text.Json.JsonSerializer.Serialize(result.Error));
                    return 1;
                }

                output.WriteLine(ValueJson.Serialize(result.Value!));
                return 0;
            }

            if (arguments is ["encode", var encodeInput, ..])
            {
                PyValue value = ValueJson.Deserialize(fileSystem.File.ReadAllText(encodeInput));
                output.WriteLine(HexFiles.Format(BlueMarshalCodec.Encode(value, profile)));
                return 0;
            }

            if (arguments is ["diff", var leftPath, var rightPath, ..])
            {
                byte[] left = HexFiles.Read(fileSystem, leftPath);
                byte[] right = HexFiles.Read(fileSystem, rightPath);
                bool semantic = arguments.Contains("--semantic", StringComparer.Ordinal);
                bool equal = semantic
                    ? SemanticEqual(left, right, profile)
                    : left.AsSpan().SequenceEqual(right);
                output.WriteLine(equal ? "equal" : "different");
                return equal ? 0 : 2;
            }

            error.WriteLine("Usage: sseproto decode|encode|diff|fixtures verify ...");
            return 64;
        }
        catch (Exception exception)
        {
            error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static bool SemanticEqual(byte[] left, byte[] right, ProtocolProfile profile)
    {
        DecodeResult<PyValue> leftValue = BlueMarshalCodec.Decode(new ReadOnlySequence<byte>(left), profile);
        DecodeResult<PyValue> rightValue = BlueMarshalCodec.Decode(new ReadOnlySequence<byte>(right), profile);
        return leftValue.IsSuccess && rightValue.IsSuccess
            && PyValueComparers.Semantic.Equals(leftValue.Value!, rightValue.Value!);
    }

    private static int ReadProfile(string[] arguments)
    {
        int option = Array.IndexOf(arguments, "--profile");
        return option >= 0 && option + 1 < arguments.Length
            ? int.Parse(arguments[option + 1], System.Globalization.CultureInfo.InvariantCulture)
            : ProtocolProfileCatalog.SupportedBuild;
    }

    private static string FindFixtureRoot(IFileSystem fileSystem)
    {
        for (IDirectoryInfo? directory = fileSystem.DirectoryInfo.New(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            string candidate = fileSystem.Path.Combine(directory.FullName, "fixtures", "protocol", "3396210");
            if (fileSystem.Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("Could not locate fixtures/protocol/3396210.");
    }
}
