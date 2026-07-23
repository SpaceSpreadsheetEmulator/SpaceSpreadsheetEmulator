using System.Security.Cryptography;

namespace SpaceSpreadsheetEmulator.Protocol.Crypto;

public sealed class AesCbcFrameCipher : IDisposable
{
    public const int KeyLength = 32;
    public const int BlockLength = 16;
    private readonly byte[] key;
    private readonly byte[] inboundIv;
    private readonly byte[] outboundIv;
    private readonly object inboundLock = new();
    private readonly object outboundLock = new();
    private bool disposed;

    public AesCbcFrameCipher(ReadOnlySpan<byte> key, ReadOnlySpan<byte> initializationVector)
    {
        if (key.Length != KeyLength)
        {
            throw new ArgumentException($"AES-256 requires exactly {KeyLength} key bytes.", nameof(key));
        }

        if (initializationVector.Length != BlockLength)
        {
            throw new ArgumentException($"AES-CBC requires exactly {BlockLength} IV bytes.", nameof(initializationVector));
        }

        this.key = key.ToArray();
        inboundIv = initializationVector.ToArray();
        outboundIv = initializationVector.ToArray();
    }

    public byte[] Encrypt(ReadOnlySpan<byte> plaintext)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        lock (outboundLock)
        {
            using Aes aes = Aes.Create();
            aes.Key = key;
            byte[] encrypted = aes.EncryptCbc(plaintext, outboundIv, PaddingMode.PKCS7);
            encrypted.AsSpan(^BlockLength).CopyTo(outboundIv);
            return encrypted;
        }
    }

    public DecodeResult<CipherPayload> Decrypt(ReadOnlySpan<byte> ciphertext)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (ciphertext.IsEmpty || ciphertext.Length % BlockLength != 0)
        {
            return Failure(
                ProtocolErrorCodes.InvalidCiphertext,
                "Encrypted frame payloads must be a non-empty multiple of the AES block size.");
        }

        lock (inboundLock)
        {
            try
            {
                using Aes aes = Aes.Create();
                aes.Key = key;
                byte[] plaintext = aes.DecryptCbc(ciphertext, inboundIv, PaddingMode.PKCS7);
                ciphertext[^BlockLength..].CopyTo(inboundIv);
                return DecodeResult<CipherPayload>.Success(new CipherPayload(plaintext));
            }
            catch (CryptographicException)
            {
                return Failure(ProtocolErrorCodes.InvalidPadding, "The encrypted frame has invalid CBC padding.");
            }
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(key);
        CryptographicOperations.ZeroMemory(inboundIv);
        CryptographicOperations.ZeroMemory(outboundIv);
        disposed = true;
    }

    private static DecodeResult<CipherPayload> Failure(string code, string message)
        => DecodeResult<CipherPayload>.Failure(new ProtocolError(code, 0, "$crypto", message));
}

public sealed record CipherPayload(byte[] Bytes);
