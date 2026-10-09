using System.Security.Cryptography;

namespace SkillSamples.Encryption;

public interface IEncryptor
{
    byte[] Encrypt(byte[] plaintext, byte[]? associatedData = null);
    byte[] Decrypt(byte[] encrypted, byte[]? associatedData = null);
}

public sealed class AesGcmEncryptor : IEncryptor, IDisposable
{
    public const int Overhead = NonceSize + TagSize;   // 28 bytes added to every value

    private const int NonceSize = 12;   // 96 bits, the size AES-GCM is designed for
    private const int TagSize = 16;     // 128 bits, the full authentication tag
    private const int KeySize = 32;     // 256 bits, AES-256

    private readonly AesGcm _aesGcm;

    public AesGcmEncryptor(byte[] key)
    {
        if (key.Length != KeySize)
        {
            throw new ArgumentException($"Key must be {KeySize} bytes", nameof(key));
        }

        _aesGcm = new AesGcm(key, TagSize);
    }

    // associatedData is not encrypted but is authenticated: pass what the value belongs to (table, column,
    // row id), so a ciphertext copied onto another row fails to decrypt instead of showing that row's data.
    public byte[] Encrypt(byte[] plaintext, byte[]? associatedData = null)
    {
        // Format: [nonce (12)] [tag (16)] [ciphertext (N)]
        var result = new byte[Overhead + plaintext.Length];
        var nonce = result.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);   // a fresh random nonce for every call, never reused

        _aesGcm.Encrypt(nonce, plaintext, result.AsSpan(Overhead), result.AsSpan(NonceSize, TagSize), associatedData);
        return result;
    }

    public byte[] Decrypt(byte[] encrypted, byte[]? associatedData = null)
    {
        if (encrypted.Length < Overhead)
        {
            throw new CryptographicException("Ciphertext is too short.");
        }

        var plaintext = new byte[encrypted.Length - Overhead];
        _aesGcm.Decrypt(
            encrypted.AsSpan(0, NonceSize), encrypted.AsSpan(Overhead), encrypted.AsSpan(NonceSize, TagSize),
            plaintext, associatedData);   // throws AuthenticationTagMismatchException if anything was changed
        return plaintext;
    }

    public void Dispose() => _aesGcm.Dispose();
}
