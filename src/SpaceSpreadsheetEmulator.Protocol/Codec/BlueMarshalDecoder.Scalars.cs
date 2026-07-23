using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Protocol.Codec;

internal ref partial struct BlueMarshalDecoder
{
    private PyValue ReadScalar(byte opcode, string path) => opcode switch
    {
        BlueOpcodes.Int8 => new PyInteger(unchecked((sbyte)ReadByte(path)), PyIntegerEncoding.Int8),
        BlueOpcodes.Int16 => new PyInteger(ReadInt16(path), PyIntegerEncoding.Int16),
        BlueOpcodes.Int32 => new PyInteger(ReadInt32(path), PyIntegerEncoding.Int32),
        BlueOpcodes.Int64 => new PyInteger(ReadInt64(path), PyIntegerEncoding.Int64),
        BlueOpcodes.BigInteger => ReadBigInteger(path),
        BlueOpcodes.Float64 => new PyFloat(BitConverter.Int64BitsToDouble(ReadInt64(path))),
        BlueOpcodes.Byte => new PyByte(ReadByte(path)),
        BlueOpcodes.Token => new PyToken(ReadAscii(ReadBoundedLength(profile.Limits.MaximumValueBytes, path), path)),
        BlueOpcodes.LongBuffer or BlueOpcodes.ShortBuffer or BlueOpcodes.Buffer => new PyBuffer(
            ImmutableArray.Create(ReadBytes(ReadBoundedLength(profile.Limits.MaximumValueBytes, path), path))),
        BlueOpcodes.EmptyBuffer => new PyBuffer(ImmutableArray<byte>.Empty),
        BlueOpcodes.Utf16Text => new PyText(ReadUtf16(ReadBoundedCharacterCount(path), path)),
        BlueOpcodes.EmptyText => new PyText(string.Empty),
        BlueOpcodes.TextCharacter => new PyText(ReadUtf16(1, path)),
        BlueOpcodes.Text => new PyText(ReadUtf8(ReadBoundedLength(profile.Limits.MaximumValueBytes, path), path)),
        _ => throw new UnreachableException(),
    };

    private PyBigInteger ReadBigInteger(string path)
    {
        int length = ReadBoundedLength(profile.Limits.MaximumValueBytes, path);
        return new PyBigInteger(new BigInteger(ReadBytes(length, path), isUnsigned: false, isBigEndian: false));
    }

    private int ReadBoundedCharacterCount(string path)
    {
        uint count = ReadSize(path);
        if (count > profile.Limits.MaximumValueBytes / 2)
        {
            Fail(ProtocolErrorCodes.LimitExceeded, path, "UTF-16 text exceeds the configured byte limit.");
        }

        return (int)count;
    }

    private string ReadAscii(int length, string path)
    {
        byte[] bytes = ReadBytes(length, path);
        if (bytes.Any(value => value > 0x7F))
        {
            Fail(ProtocolErrorCodes.InvalidValue, path, "A token may contain only ASCII bytes.");
        }

        return System.Text.Encoding.ASCII.GetString(bytes);
    }

    private short ReadInt16(string path)
    {
        Span<byte> bytes = stackalloc byte[sizeof(short)];
        if (!reader.TryCopyTo(bytes))
        {
            Fail(ProtocolErrorCodes.Incomplete, path, "A 16-bit integer is incomplete.");
        }

        reader.Advance(sizeof(short));
        return BinaryPrimitives.ReadInt16LittleEndian(bytes);
    }

    private int ReadInt32(string path)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        if (!reader.TryCopyTo(bytes))
        {
            Fail(ProtocolErrorCodes.Incomplete, path, "A 32-bit integer is incomplete.");
        }

        reader.Advance(sizeof(int));
        return BinaryPrimitives.ReadInt32LittleEndian(bytes);
    }

    private long ReadInt64(string path)
    {
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        if (!reader.TryCopyTo(bytes))
        {
            Fail(ProtocolErrorCodes.Incomplete, path, "A 64-bit integer is incomplete.");
        }

        reader.Advance(sizeof(long));
        return BinaryPrimitives.ReadInt64LittleEndian(bytes);
    }
}
