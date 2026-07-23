using SpaceSpreadsheetEmulator.Protocol;
using SpaceSpreadsheetEmulator.Protocol.Crypto;

namespace SpaceSpreadsheetEmulator.Protocol.Tests.Crypto;

public class AesCbcFrameCipherTests
{
    private static readonly byte[] Key = Convert.FromHexString(
        "000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F");
    private static readonly byte[] InitializationVector = Convert.FromHexString(
        "101112131415161718191A1B1C1D1E1F");

    [Fact]
    public void EncryptMatchesIndependentAes256CbcFixture()
    {
        using var cipher = new AesCbcFrameCipher(Key, InitializationVector);

        byte[] ciphertext = cipher.Encrypt("build-3396210"u8);

        Assert.Equal("812FD7C629CDE0F6D0309F6237C4CEEA", Convert.ToHexString(ciphertext));
    }

    [Fact]
    public void SeparateInboundAndOutboundChainsRoundTripSeveralFrames()
    {
        using var sender = new AesCbcFrameCipher(Key, InitializationVector);
        using var receiver = new AesCbcFrameCipher(Key, InitializationVector);
        byte[][] plaintexts = ["first"u8.ToArray(), "second frame"u8.ToArray(), new byte[65]];

        foreach (byte[] plaintext in plaintexts)
        {
            byte[] ciphertext = sender.Encrypt(plaintext);
            DecodeResult<CipherPayload> decoded = receiver.Decrypt(ciphertext);
            Assert.True(decoded.IsSuccess, decoded.Error?.ToString());
            Assert.Equal(plaintext, decoded.Value!.Bytes);
        }
    }

    [Fact]
    public void InvalidBlockLengthReturnsStableError()
    {
        using var cipher = new AesCbcFrameCipher(Key, InitializationVector);

        DecodeResult<CipherPayload> decoded = cipher.Decrypt([0x01, 0x02]);

        Assert.False(decoded.IsSuccess);
        Assert.Equal(ProtocolErrorCodes.InvalidCiphertext, decoded.Error!.Code);
        Assert.Equal("$crypto", decoded.Error.ValuePath);
    }
}
