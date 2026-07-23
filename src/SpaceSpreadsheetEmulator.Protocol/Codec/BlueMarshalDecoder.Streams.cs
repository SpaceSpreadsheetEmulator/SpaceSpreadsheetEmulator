using System.Collections.Immutable;
using System.Diagnostics;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Protocol.Codec;

internal ref partial struct BlueMarshalDecoder
{
    private PyValue ReadStream(byte opcode, int depth, string path)
    {
        if (opcode == BlueOpcodes.Substructure)
        {
            return new PySubstructure(ReadValue(depth + 1, $"{path}.value"));
        }

        if (opcode == BlueOpcodes.ChecksummedStream)
        {
            uint checksum = ReadUInt32($"{path}.checksum");
            return new PyChecksummedStream(checksum, ReadValue(depth + 1, $"{path}.value"));
        }

        int dataLength = ReadBoundedLength(profile.Limits.MaximumValueBytes, path);
        ImmutableArray<byte> data = ImmutableArray.Create(ReadBytes(dataLength, path));
        return opcode switch
        {
            BlueOpcodes.Substream => new PySubstream(data),
            BlueOpcodes.OpaquePickedData => new PyOpaquePickedData(data),
            _ => throw new UnreachableException(),
        };
    }
}
