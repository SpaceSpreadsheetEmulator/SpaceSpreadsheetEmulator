using SpaceSpreadsheetEmulator.CaptureInspector.Services;
using SpaceSpreadsheetEmulator.CaptureInspector.Models;

namespace SpaceSpreadsheetEmulator.CaptureInspector.Tests;

public sealed class CaptureFramesReaderTests
{
    [Fact]
    public async Task ReadAsyncKeepsValidFramesAndReportsInvalidLines()
    {
        using var fixture = TemporaryFile.Create(
            """
            {"decoder_schema_version":2,"frame_index":7,"direction":"outbound","start_relative_ms":11.25,"end_relative_ms":12.5,"stream_frame_index":3,"wire_size":21,"message_type":"CALL_REQ","service":"machoNet","method":"GetTime","call_id":4,"decode_status":"marshal_decoded","record_kind":"protocol_frame","raw_base64":"AQI=","decoded_payload":{"typeID":34}}
            {"decoder_schema_version":2,"frame_index":8,"direction":"inbound","call_id":null,"wire_size":null}
            not json
            {"decoder_schema_version":1,"frame_index":8}
            """);

        var result = await CaptureFramesReader.ReadAsync(fixture.Path, 100);

        CaptureFrame frame = result.Frames[0];
        Assert.Equal(7, frame.FrameIndex);
        Assert.Equal("machoNet.GetTime", frame.ServiceMethod);
        Assert.Equal(11.25, frame.RelativeMilliseconds);
        Assert.Equal(12.5, frame.EndRelativeMilliseconds);
        Assert.Equal("1.25 ms", frame.DurationDisplay);
        Assert.Equal("AQI=", frame.RawBase64);
        Assert.Equal(2, result.Frames.Count);
        Assert.Null(result.Frames[1].CallId);
        Assert.Null(result.Frames[1].WireSize);
        Assert.Equal(2, result.Diagnostics.Count);
        Assert.False(result.IsTruncated);
    }

    [Fact]
    public async Task ReadAsyncMarksAReachedBoundAsTruncated()
    {
        using var fixture = TemporaryFile.Create(
            """
            {"decoder_schema_version":2,"frame_index":1}
            {"decoder_schema_version":2,"frame_index":2}
            """);

        var result = await CaptureFramesReader.ReadAsync(fixture.Path, 1);

        Assert.Single(result.Frames);
        Assert.True(result.IsTruncated);
    }

    private sealed class TemporaryFile : IDisposable
    {
        private TemporaryFile(string path) => Path = path;

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
