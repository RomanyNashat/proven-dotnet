using System.Text;

namespace SkillSamples.Encryption;

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
