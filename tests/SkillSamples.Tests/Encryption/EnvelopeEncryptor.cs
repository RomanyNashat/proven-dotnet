using System.Security.Cryptography;

namespace SkillSamples.Encryption;

// The vault holds the key-encryption key (KEK) and never releases it; it only wraps and unwraps DEKs.
public interface IKeyVaultService
{
    Task<byte[]> EncryptAsync(string kekId, byte[] data, CancellationToken ct);
    Task<byte[]> DecryptAsync(string kekId, byte[] data, CancellationToken ct);
}

public sealed record EncryptedEnvelope(string KekId, string EncryptedDek, string EncryptedData);

public sealed class EnvelopeEncryptor(IKeyVaultService keyVault)
{
    public async Task<EncryptedEnvelope> EncryptAsync(byte[] plaintext, string kekId, CancellationToken ct)
    {
        var dek = RandomNumberGenerator.GetBytes(32);   // a new data-encryption key per record or batch
        try
        {
            using var aes = new AesGcmEncryptor(dek);
            var encryptedData = aes.Encrypt(plaintext);
            var encryptedDek = await keyVault.EncryptAsync(kekId, dek, ct);
            return new EncryptedEnvelope(kekId, Convert.ToBase64String(encryptedDek), Convert.ToBase64String(encryptedData));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dek);   // even when the vault call fails
        }
    }

    public async Task<byte[]> DecryptAsync(EncryptedEnvelope envelope, CancellationToken ct)
    {
        var dek = await keyVault.DecryptAsync(envelope.KekId, Convert.FromBase64String(envelope.EncryptedDek), ct);
        try
        {
            using var aes = new AesGcmEncryptor(dek);
            return aes.Decrypt(Convert.FromBase64String(envelope.EncryptedData));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dek);
        }
    }
}
