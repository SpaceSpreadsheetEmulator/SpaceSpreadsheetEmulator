namespace SpaceSpreadsheetEmulator.Protocol.Codec;

internal static class BlueOpcodes
{
    public const byte TypeMask = 0x3F;
    public const byte SaveFlag = 0x40;
    public const byte Null = 0x01;
    public const byte Token = 0x02;
    public const byte Int64 = 0x03;
    public const byte Int32 = 0x04;
    public const byte Int16 = 0x05;
    public const byte Int8 = 0x06;
    public const byte MinusOne = 0x07;
    public const byte Zero = 0x08;
    public const byte One = 0x09;
    public const byte Float64 = 0x0A;
    public const byte ZeroFloat = 0x0B;
    public const byte LongBuffer = 0x0D;
    public const byte EmptyBuffer = 0x0E;
    public const byte Byte = 0x0F;
    public const byte ShortBuffer = 0x10;
    public const byte StringTableReference = 0x11;
    public const byte Utf16Text = 0x12;
    public const byte Buffer = 0x13;
    public const byte Tuple = 0x14;
    public const byte List = 0x15;
    public const byte Dictionary = 0x16;
    public const byte Object = 0x17;
    public const byte Substructure = 0x19;
    public const byte SavedValueReference = 0x1B;
    public const byte ChecksummedStream = 0x1C;
    public const byte True = 0x1F;
    public const byte False = 0x20;
    public const byte OpaquePickedData = 0x21;
    public const byte ExtendedObject1 = 0x22;
    public const byte ExtendedObject2 = 0x23;
    public const byte EmptyTuple = 0x24;
    public const byte TupleOne = 0x25;
    public const byte EmptyList = 0x26;
    public const byte ListOne = 0x27;
    public const byte EmptyText = 0x28;
    public const byte TextCharacter = 0x29;
    public const byte PackedRow = 0x2A;
    public const byte Substream = 0x2B;
    public const byte TupleTwo = 0x2C;
    public const byte Terminator = 0x2D;
    public const byte Text = 0x2E;
    public const byte BigInteger = 0x2F;
}
