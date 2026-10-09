using System.Security.Cryptography;

namespace SkillSamples.Encryption;

// One version byte in front of every value, so the format can change without a big-bang migration.
// New values are always AES-GCM; old AES-CBC values still decrypt until they've been re-encrypted.
public sealed class VersionedEncryptor(byte[] key) : IEncryptor
{
    private const byte VersionCbc = 0x01;
    private const byte VersionGcm = 0x02;

    public byte[] Encrypt(byte[] plaintext, byte[]? associatedData = null)
    {
        using var gcm = new AesGcmEncryptor(key);
        var encrypted = gcm.Encrypt(plaintext, associatedData);
        var result = new byte[1 + encrypted.Length];
        result[0] = VersionGcm;
        encrypted.CopyTo(result, 1);
        return result;
    }

    public byte[] Decrypt(byte[] encrypted, byte[]? associatedData = null)
    {
        if (encrypted.Length == 0)
        {
            throw new CryptographicException("Ciphertext is empty.");
        }

        var data = encrypted.AsSpan(1).ToArray();
        return encrypted[0] switch
        {
            VersionGcm => DecryptGcm(data, associatedData),
            VersionCbc => DecryptLegacyCbc(data),
            var v => throw new CryptographicException($"Unknown encryption version: {v}"),
        };
    }

    private byte[] DecryptGcm(byte[] data, byte[]? associatedData)
    {
        using var gcm = new AesGcmEncryptor(key);
        return gcm.Decrypt(data, associatedData);
    }

    // Legacy only. CBC has no authentication: never write it, and never return a different error for bad
    // padding than for any other failure (that difference is what a padding-oracle attack reads).
    private byte[] DecryptLegacyCbc(byte[] data)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        try
        {
            return aes.DecryptCbc(data.AsSpan(16), data.AsSpan(0, 16), PaddingMode.PKCS7);
        }
        catch (CryptographicException)
        {
            throw new CryptographicException("Decryption failed.");
        }
    }
}
