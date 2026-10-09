---
name: encryption-patterns
description: Encryption for .NET: AES-GCM with associated data, envelope encryption (DEK/KEK), PBKDF2, a versioned format for migrations, key rotation. Core code tested in CI.
version: 1.1.0
---

# Encryption Patterns

The code in this skill is compiled and tested in CI (`tests/SkillSamples.Tests/Encryption`): tampering,
moved ciphertext, wrong keys, legacy decryption and key zeroing all have a test.

## AES-GCM: the default for data at rest

<!-- sample: tests/SkillSamples.Tests/Encryption/AesGcmEncryptor.cs -->
```csharp
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
```

**Associated data is the part people skip.** Without it, an attacker with write access to the table can
copy patient A's encrypted national ID onto patient B's row, and the app decrypts it happily. Passing
`"patients.national_id:{id}"` as associated data makes that copy fail to decrypt.

## Strings, and sizing the column

<!-- sample: tests/SkillSamples.Tests/Encryption/StringEncryptor.cs -->
```csharp
public sealed class StringEncryptor(IEncryptor encryptor)
{
    public string Encrypt(string plaintext, string context) =>
        Convert.ToBase64String(encryptor.Encrypt(Encoding.UTF8.GetBytes(plaintext), Encoding.UTF8.GetBytes(context)));

    public string Decrypt(string base64Ciphertext, string context) =>
        Encoding.UTF8.GetString(encryptor.Decrypt(Convert.FromBase64String(base64Ciphertext), Encoding.UTF8.GetBytes(context)));

    // Column size for a value of up to maxPlaintextBytes: varchar(n), never a binary column (column rules).
    public static int ColumnLength(int maxPlaintextBytes, int overhead = AesGcmEncryptor.Overhead) =>
        4 * (int)Math.Ceiling((maxPlaintextBytes + overhead) / 3.0);
}
```

Ciphertext is stored as Base64 in a bounded `varchar(n)`, never a binary column (column rule). AES-GCM
adds 28 bytes, and Base64 makes it 4/3 larger, so `n = 4 × ceil((maxPlaintextBytes + 28) / 3)`: a
100-byte value needs `varchar(172)`. With the version byte below, use 29 instead of 28. Size by
**bytes**, not characters: an Arabic character is 2 bytes in UTF-8.

## Envelope encryption (DEK/KEK)

The data key (DEK) encrypts the data; the vault's key (KEK) encrypts the DEK, and never leaves the
vault. Rotating the KEK re-wraps the small DEKs, not the data.

<!-- sample: tests/SkillSamples.Tests/Encryption/EnvelopeEncryptor.cs -->
```csharp
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
```

## PBKDF2: a key from a secret phrase

<!-- sample: tests/SkillSamples.Tests/Encryption/KeyDerivation.cs -->
```csharp
// Derives an encryption key from a secret phrase. Not for storing user passwords: that's ASP.NET Core
// Identity's PasswordHasher (rules/security.md).
public static class KeyDerivation
{
    public const int Iterations = 600_000;   // OWASP's figure for PBKDF2-HMAC-SHA256
    private const int SaltSize = 16;
    private const int KeySize = 32;

    public static (byte[] Key, byte[] Salt) DeriveKey(string secret)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        return (DeriveKey(secret, salt), salt);
    }

    public static byte[] DeriveKey(string secret, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(secret), salt, Iterations, HashAlgorithmName.SHA256, KeySize);
}
```

600,000 iterations is OWASP's current figure for PBKDF2-HMAC-SHA256 (Password Storage Cheat Sheet). It
costs a few hundred milliseconds, which is why it's done once at startup, not per request. Store the salt
beside the derived data; it isn't secret.

## A versioned format, for changing algorithms later

<!-- sample: tests/SkillSamples.Tests/Encryption/VersionedEncryptor.cs -->
```csharp
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
```

## Key rotation

```csharp
public sealed class KeyRotationService(
    IKeyVaultService keyVault, IEncryptedDataRepository repository, ILogger<KeyRotationService> logger)
{
    public async Task RotateKeyAsync(string oldKekId, string newKekId, CancellationToken ct)
    {
        var processed = 0;
        // The repository pages by key (WHERE kek_id = @old AND id > @lastId ORDER BY id), not by OFFSET:
        // every updated row leaves the filter, so OFFSET paging would skip half of them.
        await foreach (var batch in repository.GetBatchesAsync(oldKekId, batchSize: 100, ct))
        {
            foreach (var record in batch)
            {
                var dek = await keyVault.DecryptAsync(oldKekId, Convert.FromBase64String(record.EncryptedDek), ct);
                try
                {
                    record.EncryptedDek = Convert.ToBase64String(await keyVault.EncryptAsync(newKekId, dek, ct));
                    record.KekId = newKekId;
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(dek);
                }
            }

            await repository.UpdateBatchAsync(batch, ct);
            processed += batch.Count;
            logger.LogInformation("Key rotation: {Count} records moved from {OldKek} to {NewKek}", processed, oldKekId, newKekId);
        }
    }
}
```

Keep the old KEK enabled until the job reports zero records left on it. A rotation that stops halfway is
fine: each record says which KEK wraps it.

## Rules
- Never reuse a nonce: `RandomNumberGenerator.Fill()` on every encryption (the encryptor does it).
- Pass associated data that ties the value to its row and column.
- Never put keys in config files or source; use the vault (`secret-management`).
- Ciphertext goes in a bounded `varchar(n)` as Base64, sized with the formula above.
- Authenticated encryption only (AES-GCM). AES-CBC is for reading legacy data, never for writing.
- Zero key material after use, in a `finally`: `CryptographicOperations.ZeroMemory(key)`.
- Version the format from day one.
- PBKDF2-HMAC-SHA256: 600,000 iterations. User passwords go through ASP.NET Core Identity, not this.
