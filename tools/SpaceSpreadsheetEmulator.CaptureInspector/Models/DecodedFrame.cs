using SpaceSpreadsheetEmulator.Protocol;
using SpaceSpreadsheetEmulator.Protocol.MachoNet;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.CaptureInspector.Models;

public sealed record DecodedFrame(
    byte[] CapturedBytes,
    byte[] DecodedBytes,
    bool WasDecompressed,
    PyValue? RootValue,
    MachoPacket? MachoPacket,
    ProtocolError? Error)
{
    public bool HasDifferentCapturedBytes => WasDecompressed;
}

public sealed record PacketTreeBuildResult(
    IReadOnlyList<DecodeTreeNode> Nodes,
    DecodedFrame Frame,
    int? ClientBuild);
