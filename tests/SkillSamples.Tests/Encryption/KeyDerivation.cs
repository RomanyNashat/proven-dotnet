using System.Security.Cryptography;
using System.Text;

namespace SkillSamples.Encryption;

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
