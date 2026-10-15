using System.Security.Cryptography;
using System.Text;
using SkillSamples.Production;
using Xunit;

namespace SkillSamples.Encryption;

// Stands in for the vault: wraps DEKs with its own AES-GCM key and remembers what it was handed.
public sealed class FakeKeyVault : IKeyVaultService, IDisposable
{
    private readonly AesGcmEncryptor _kek = new(RandomNumberGenerator.GetBytes(32));

    public byte[]? LastWrapped { get; private set; }
    public bool FailNextEncrypt { get; set; }

    public Task<byte[]> EncryptAsync(string kekId, byte[] data, CancellationToken ct)
    {
        LastWrapped = data;   // keep the caller's array, to check it gets zeroed
        if (FailNextEncrypt)
        {
            throw new InvalidOperationException("vault unavailable");
        }

        return Task.FromResult(_kek.Encrypt(data, Encoding.UTF8.GetBytes(kekId)));
    }

    public Task<byte[]> DecryptAsync(string kekId, byte[] data, CancellationToken ct) =>
        Task.FromResult(_kek.Decrypt(data, Encoding.UTF8.GetBytes(kekId)));

    public void Dispose() => _kek.Dispose();
}

public sealed class EncryptionTests
{
    private static readonly byte[] Key = RandomNumberGenerator.GetBytes(32);

    [Fact]
    public void AesGcm_RoundTrips_AndNeverRepeatsCiphertext()
    {
        using var aes = new AesGcmEncryptor(Key);
        var plaintext = Encoding.UTF8.GetBytes("1089234567");

        var first = aes.Encrypt(plaintext);
        var second = aes.Encrypt(plaintext);

        Assert.Equal(plaintext, aes.Decrypt(first));
        Assert.NotEqual(first, second);   // fresh nonce each time
        Assert.Equal(plaintext.Length + 28, first.Length);
    }

    [Fact]
    public void AesGcm_AnyChangedByte_FailsToDecrypt()
    {
        using var aes = new AesGcmEncryptor(Key);
        var encrypted = aes.Encrypt(Encoding.UTF8.GetBytes("diagnosis"));

        for (var i = 0; i < encrypted.Length; i++)
        {
            var tampered = (byte[])encrypted.Clone();
            tampered[i] ^= 0x01;
            Assert.ThrowsAny<CryptographicException>(() => aes.Decrypt(tampered));
        }
    }

    [Fact]
    public void AesGcm_CiphertextMovedToAnotherRow_FailsToDecrypt()
    {
        var strings = new StringEncryptor(new AesGcmEncryptor(Key));
        var forRow7 = strings.Encrypt("1089234567", "patients.national_id:7");

        Assert.Equal("1089234567", strings.Decrypt(forRow7, "patients.national_id:7"));
        Assert.ThrowsAny<CryptographicException>(() => strings.Decrypt(forRow7, "patients.national_id:8"));
    }

    [Fact]
    public void AesGcm_TooShortOrWrongKey_IsRejected()
    {
        using var aes = new AesGcmEncryptor(Key);
        Assert.Throws<CryptographicException>(() => aes.Decrypt(new byte[10]));
        Assert.Throws<ArgumentException>(() => new AesGcmEncryptor(new byte[16]));
    }

    [Theory]
    [InlineData(100, 172)]
    [InlineData(10, 52)]
    [InlineData(0, 40)]
    public void ColumnLength_FitsTheLongestValue(int maxBytes, int expected)
    {
        var strings = new StringEncryptor(new AesGcmEncryptor(Key));
        var longest = strings.Encrypt(new string('x', maxBytes), "ctx");

        Assert.Equal(expected, StringEncryptor.ColumnLength(maxBytes));
        Assert.True(longest.Length <= expected);
    }

    [Fact]
    public async Task Envelope_RoundTrips_AndZeroesTheDek()
    {
        using var vault = new FakeKeyVault();
        var envelope = new EnvelopeEncryptor(vault);

        var sealedData = await envelope.EncryptAsync(Encoding.UTF8.GetBytes("lab result"), "kek-2026", default);

        Assert.All(vault.LastWrapped!, b => Assert.Equal(0, b));
        Assert.Equal("lab result", Encoding.UTF8.GetString(await envelope.DecryptAsync(sealedData, default)));
    }

    [Fact]
    public async Task Envelope_VaultFails_DekIsStillZeroed()
    {
        using var vault = new FakeKeyVault { FailNextEncrypt = true };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new EnvelopeEncryptor(vault).EncryptAsync([1, 2, 3], "kek-2026", default));
        Assert.All(vault.LastWrapped!, b => Assert.Equal(0, b));
    }

    [Fact]
    public void Pbkdf2_SameSaltSameKey_NewSaltNewKey()
    {
        var (key, salt) = KeyDerivation.DeriveKey("a long secret phrase");

        Assert.Equal(key, KeyDerivation.DeriveKey("a long secret phrase", salt));
        Assert.NotEqual(key, KeyDerivation.DeriveKey("a long secret phrase").Key);
        Assert.Equal(600_000, KeyDerivation.Iterations);
    }

    [Fact]
    public void Versioned_WritesGcm_ReadsLegacyCbc_RejectsUnknown()
    {
        var versioned = new VersionedEncryptor(Key);
        var plaintext = Encoding.UTF8.GetBytes("old record");

        var current = versioned.Encrypt(plaintext);
        Assert.Equal(0x02, current[0]);
        Assert.Equal(plaintext, versioned.Decrypt(current));

        using var aes = Aes.Create();
        aes.Key = Key;
        var iv = RandomNumberGenerator.GetBytes(16);
        byte[] legacy = [0x01, .. iv, .. aes.EncryptCbc(plaintext, iv, PaddingMode.PKCS7)];
        Assert.Equal(plaintext, versioned.Decrypt(legacy));

        Assert.Throws<CryptographicException>(() => versioned.Decrypt([0x09, 1, 2, 3]));
        Assert.Throws<CryptographicException>(() => versioned.Decrypt([]));
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public void Story_ALegacyNationalIdIsReadAndSavedAgain_NowItsBoundToItsRow_AndACopyIntoAnotherRowFails()
    {
        var versioned = new VersionedEncryptor(Key);
        byte[] Row(int id) => Encoding.UTF8.GetBytes($"patients:{id}:national_id");
        var nationalId = Encoding.UTF8.GetBytes("1089234567");

        // Given: patient 7's national id, written last year in the old CBC format
        using var aes = Aes.Create();
        aes.Key = Key;
        var iv = RandomNumberGenerator.GetBytes(16);
        byte[] legacy = [0x01, .. iv, .. aes.EncryptCbc(nationalId, iv, PaddingMode.PKCS7)];

        // When: the service reads it and saves the record again, and later someone copies the stored
        // value into patient 8's row
        var read = versioned.Decrypt(legacy, Row(7));
        var resaved = versioned.Encrypt(read, Row(7));

        // Then: the old value was readable from any row (it isn't bound to one), the new one is GCM and
        // bound to row 7, and in row 8 it fails instead of showing patient 7's id as patient 8's
        Assert.Equal(nationalId, versioned.Decrypt(legacy, Row(8)));
        Assert.Equal(0x02, resaved[0]);
        Assert.Equal(nationalId, versioned.Decrypt(resaved, Row(7)));
        Assert.ThrowsAny<CryptographicException>(() => versioned.Decrypt(resaved, Row(8)));
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Production)]
    public void Production_NoIcuNoTzdata_ArabicValuesRoundTrip_AndFitTheirColumn()
    {
        ProductionConditions.Require();
        var strings = new StringEncryptor(new VersionedEncryptor(Key));
        const string name = "عبدالرحمن بن محمد";

        var stored = strings.Encrypt(name, "patients:7:name");

        Assert.Equal(name, strings.Decrypt(stored, "patients:7:name"));
        Assert.True(stored.Length <= StringEncryptor.ColumnLength(Encoding.UTF8.GetByteCount(name), AesGcmEncryptor.Overhead + 1),
            $"{stored.Length} chars");
    }
}
