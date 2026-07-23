using SpaceSpreadsheetEmulator.Protocol.Tool;

namespace SpaceSpreadsheetEmulator.Protocol.Tool.Tests.Commands;

public class CommandLineTests
{
    [Fact]
    public void DecodePrintsSemanticJson()
    {
        using var fixture = TemporaryFile.Create("7e 00 00 00 00 06 2a");
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = CommandLine.Run(["decode", fixture.Path], output, error);

        Assert.Equal(0, exitCode);
        Assert.Contains("\"kind\": \"integer\"", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("\"value\": 42", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public void EncodeAcceptsDecodedObjectVocabulary()
    {
        const string Json = """
            {
              "kind": "object",
              "type": { "kind": "token", "value": "T" },
              "state": { "kind": "null" }
            }
            """;
        using var fixture = TemporaryFile.Create(Json);
        using var output = new StringWriter();

        int exitCode = CommandLine.Run(["encode", fixture.Path], output, TextWriter.Null);

        Assert.Equal(0, exitCode);
        Assert.Equal("7e000000001702015401", output.ToString().Trim());
    }

    [Fact]
    public void SemanticDiffIgnoresIntegerWidthWhileByteDiffDoesNot()
    {
        using var narrow = TemporaryFile.Create("7e 00 00 00 00 06 01");
        using var wide = TemporaryFile.Create("7e 00 00 00 00 03 01 00 00 00 00 00 00 00");

        Assert.Equal(2, CommandLine.Run(["diff", narrow.Path, wide.Path], TextWriter.Null, TextWriter.Null));
        Assert.Equal(0, CommandLine.Run(
            ["diff", narrow.Path, wide.Path, "--semantic"],
            TextWriter.Null,
            TextWriter.Null));
    }

    private sealed class TemporaryFile : IDisposable
    {
        private TemporaryFile(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryFile Create(string content)
        {
            string path = System.IO.Path.GetTempFileName();
            File.WriteAllText(path, content);
            return new TemporaryFile(path);
        }

        public void Dispose() => File.Delete(Path);
    }
}
