using System.Security.Cryptography;
using System.Text;
using SkillSamples.Encryption;

namespace SkillSamples.PiiMasking;

public interface ITokenRepository
{
    Task StoreAsync(string token, byte[] encrypted, string fieldType, CancellationToken ct);   // upsert
    Task<byte[]?> GetAsync(string token, CancellationToken ct);
}

// Replaces a PII value with a token. The same value always gets the same token, so lookups by value work.
// The token is an HMAC with a secret key, not a plain hash: a national ID has only 10^10 values, so a
// plain SHA-256 of one is reversed by hashing them all.
public sealed class PiiTokenizer(byte[] tokenKey, IEncryptor encryptor, ITokenRepository tokens)
{
    public async Task<string> TokenizeAsync(string plaintext, string fieldType, CancellationToken ct)
    {
        var token = Token(plaintext, fieldType);
        var encrypted = encryptor.Encrypt(Encoding.UTF8.GetBytes(plaintext), Encoding.UTF8.GetBytes(token));
        await tokens.StoreAsync(token, encrypted, fieldType, ct);
        return token;
    }

    public async Task<string?> DetokenizeAsync(string token, CancellationToken ct)
    {
        var encrypted = await tokens.GetAsync(token, ct);
        return encrypted is null
            ? null
            : Encoding.UTF8.GetString(encryptor.Decrypt(encrypted, Encoding.UTF8.GetBytes(token)));
    }

    public string Token(string plaintext, string fieldType) =>
        "tok_" + Convert.ToHexStringLower(HMACSHA256.HashData(tokenKey, Encoding.UTF8.GetBytes($"{fieldType}:{plaintext}")))[..32];
}
