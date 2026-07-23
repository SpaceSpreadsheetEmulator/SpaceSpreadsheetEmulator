using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using SpaceSpreadsheetEmulator.Protocol.Profiles;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Protocol.Codec;

internal ref partial struct BlueMarshalDecoder
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly UnicodeEncoding StrictUtf16 = new(false, false, true);
    private readonly ReadOnlySequence<byte> input;
    private readonly ProtocolProfile profile;
    private readonly List<PyValue> savedValues;
    private SequenceReader<byte> reader;
    private uint declaredSavedValueCount;

    public BlueMarshalDecoder(ReadOnlySequence<byte> input, ProtocolProfile profile)
    {
        this.input = input;
        this.profile = profile;
        reader = new SequenceReader<byte>(input);
        savedValues = [];
        declaredSavedValueCount = 0;
    }

    public PyValue ReadRoot()
    {
        if (ReadByte("$") != 0x7E)
        {
            Fail(ProtocolErrorCodes.InvalidValue, "$", "The marshal stream does not begin with 0x7E.");
        }

        declaredSavedValueCount = ReadUInt32("$.savedValueCount");
        if (declaredSavedValueCount > profile.Limits.MaximumSavedReferences)
        {
            Fail(ProtocolErrorCodes.LimitExceeded, "$.savedValueCount", "The declared saved-value table exceeds the configured limit.");
        }

        PyValue value = ReadValue(0, "$");
        if (!reader.End)
        {
            Fail(ProtocolErrorCodes.TrailingData, "$", "Trailing bytes follow the root value.");
        }

        if (savedValues.Count > declaredSavedValueCount)
        {
            Fail(ProtocolErrorCodes.InvalidReference, "$", "More values were saved than the stream header declared.");
        }

        return value with
        {
            WireForm = new PyWireForm(value.WireForm?.Opcode ?? 0, ImmutableArray.Create(input.ToArray())),
        };
    }

    private PyValue ReadValue(int depth, string path)
    {
        if (depth > profile.Limits.MaximumNestingDepth)
        {
            Fail(ProtocolErrorCodes.LimitExceeded, path, "The maximum nesting depth was exceeded.");
        }

        long start = reader.Consumed;
        byte encodedOpcode = ReadByte(path);
        bool save = (encodedOpcode & BlueOpcodes.SaveFlag) != 0;
        byte opcode = (byte)(encodedOpcode & BlueOpcodes.TypeMask);

        PyValue value = opcode switch
        {
            BlueOpcodes.Null => PyNull.Instance,
            BlueOpcodes.False => new PyBoolean(false),
            BlueOpcodes.True => new PyBoolean(true),
            BlueOpcodes.MinusOne => new PyInteger(-1, PyIntegerEncoding.Int8),
            BlueOpcodes.Zero => new PyInteger(0, PyIntegerEncoding.Int8),
            BlueOpcodes.One => new PyInteger(1, PyIntegerEncoding.Int8),
            BlueOpcodes.ZeroFloat => new PyFloat(0),
            BlueOpcodes.Int8 or BlueOpcodes.Int16 or BlueOpcodes.Int32 or BlueOpcodes.Int64
                or BlueOpcodes.BigInteger or BlueOpcodes.Float64 or BlueOpcodes.Byte
                or BlueOpcodes.Token or BlueOpcodes.LongBuffer or BlueOpcodes.EmptyBuffer
                or BlueOpcodes.ShortBuffer or BlueOpcodes.Buffer or BlueOpcodes.Utf16Text
                or BlueOpcodes.EmptyText or BlueOpcodes.TextCharacter or BlueOpcodes.Text
                => ReadScalar(opcode, path),
            BlueOpcodes.Tuple or BlueOpcodes.TupleOne or BlueOpcodes.TupleTwo or BlueOpcodes.EmptyTuple
                or BlueOpcodes.List or BlueOpcodes.ListOne or BlueOpcodes.EmptyList
                or BlueOpcodes.Dictionary => ReadCollection(opcode, depth, path),
            BlueOpcodes.StringTableReference or BlueOpcodes.SavedValueReference
                or BlueOpcodes.Object or BlueOpcodes.ExtendedObject1 or BlueOpcodes.ExtendedObject2
                => ReadReferenceOrObject(opcode, depth, path),
            BlueOpcodes.Substructure or BlueOpcodes.Substream
                or BlueOpcodes.ChecksummedStream or BlueOpcodes.OpaquePickedData
                => ReadStream(opcode, depth, path),
            BlueOpcodes.PackedRow => ReadPackedRow(depth, path),
            _ => throw CreateError(
                ProtocolErrorCodes.UnknownOpcode,
                start,
                path,
                $"Opcode 0x{opcode:X2} is not defined by profile {profile.Build}."),
        };

        long length = reader.Consumed - start;
        value = value with
        {
            WireForm = new PyWireForm(encodedOpcode, ImmutableArray.Create(input.Slice(start, length).ToArray())),
        };

        if (save)
        {
            if (savedValues.Count >= declaredSavedValueCount)
            {
                Fail(ProtocolErrorCodes.InvalidReference, path, "A save flag exceeds the slot count declared by the stream header.");
            }

            savedValues.Add(value);
        }

        return value;
    }

    private bool NextIsTerminator()
        => reader.TryPeek(out byte value) && (value & BlueOpcodes.TypeMask) == BlueOpcodes.Terminator;

    private void ReadTerminator(string path)
    {
        byte value = ReadByte(path);
        if ((value & BlueOpcodes.TypeMask) != BlueOpcodes.Terminator || (value & BlueOpcodes.SaveFlag) != 0)
        {
            Fail(ProtocolErrorCodes.InvalidValue, path, "An extended-object terminator was expected.");
        }
    }

    private byte ReadByte(string path)
    {
        if (!reader.TryRead(out byte value))
        {
            Fail(ProtocolErrorCodes.Incomplete, path, "The encoded value ended unexpectedly.");
        }

        return value;
    }

    private uint ReadSize(string path)
    {
        byte compact = ReadByte(path);
        return compact == byte.MaxValue ? ReadUInt32(path) : compact;
    }

    private int ReadBoundedLength(int maximum, string path)
    {
        uint length = ReadSize(path);
        if (length > maximum)
        {
            Fail(ProtocolErrorCodes.LimitExceeded, path, $"Length {length} exceeds the limit {maximum}.");
        }

        if (reader.Remaining < length)
        {
            Fail(ProtocolErrorCodes.Incomplete, path, "The declared payload is incomplete.");
        }

        return (int)length;
    }

    private byte[] ReadBytes(int length, string path)
    {
        if (reader.Remaining < length)
        {
            Fail(ProtocolErrorCodes.Incomplete, path, "The declared payload is incomplete.");
        }

        byte[] bytes = GC.AllocateUninitializedArray<byte>(length);
        if (!reader.TryCopyTo(bytes))
        {
            Fail(ProtocolErrorCodes.Incomplete, path, "The declared payload is incomplete.");
        }

        reader.Advance(length);
        return bytes;
    }

    private string ReadUtf8(int length, string path)
    {
        try
        {
            return StrictUtf8.GetString(ReadBytes(length, path));
        }
        catch (DecoderFallbackException)
        {
            Fail(ProtocolErrorCodes.InvalidValue, path, "Text is not valid UTF-8.");
            return string.Empty;
        }
    }

    private string ReadUtf16(int characterCount, string path)
    {
        if (characterCount > profile.Limits.MaximumValueBytes / 2)
        {
            Fail(ProtocolErrorCodes.LimitExceeded, path, "UTF-16 text exceeds the configured byte limit.");
        }

        try
        {
            return StrictUtf16.GetString(ReadBytes(checked(characterCount * 2), path));
        }
        catch (DecoderFallbackException)
        {
            Fail(ProtocolErrorCodes.InvalidValue, path, "Text is not valid UTF-16LE.");
            return string.Empty;
        }
    }

    private uint ReadUInt32(string path)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        if (!reader.TryCopyTo(bytes))
        {
            Fail(ProtocolErrorCodes.Incomplete, path, "A 32-bit unsigned integer is incomplete.");
        }

        reader.Advance(sizeof(uint));
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes);
    }

    [DoesNotReturn]
    private void Fail(string code, string path, string message)
        => throw CreateError(code, reader.Consumed, path, message);

    private static ProtocolReadException CreateError(string code, long offset, string path, string message)
        => new(new ProtocolError(code, offset, path, message));
}
