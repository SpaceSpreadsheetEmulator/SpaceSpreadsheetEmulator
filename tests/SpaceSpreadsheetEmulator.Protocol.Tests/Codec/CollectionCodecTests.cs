using System.Buffers;
using SpaceSpreadsheetEmulator.Protocol.Codec;
using SpaceSpreadsheetEmulator.Protocol.Profiles;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Protocol.Tests.Codec;

public class CollectionCodecTests
{
    [Fact]
    public void DictionaryRetainsOrderAndDuplicateKeys()
    {
        byte[] fixture =
        [
            0x7E, 0, 0, 0, 0,
            0x16, 0x02,
            0x09, 0x2E, 0x01, 0x6B,
            0x06, 0x02, 0x2E, 0x01, 0x6B,
        ];
        DecodeResult<PyValue> result = BlueMarshalCodec.Decode(
            new ReadOnlySequence<byte>(fixture),
            ProtocolProfileCatalog.GetRequired(3_396_210));

        PyDictionary dictionary = Assert.IsType<PyDictionary>(result.Value);
        Assert.Equal(2, dictionary.Entries.Length);
        Assert.Equal(1, Assert.IsType<PyInteger>(dictionary.Entries[0].Value).Value);
        Assert.Equal(2, Assert.IsType<PyInteger>(dictionary.Entries[1].Value).Value);
    }
}
