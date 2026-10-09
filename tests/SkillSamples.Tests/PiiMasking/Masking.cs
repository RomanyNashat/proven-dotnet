namespace SkillSamples.PiiMasking;

public enum MaskingStrategy
{
    Full,       // "***MASKED***"
    Email,      // "r***@example.com"
    Phone,      // "*******89"
    LastFour,   // "******4567"
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class SensitiveAttribute(MaskingStrategy strategy = MaskingStrategy.Full) : Attribute
{
    public MaskingStrategy Strategy { get; } = strategy;
}

public static class Masking
{
    public const string Full = "***MASKED***";

    // A value too short to partly show is masked completely: showing "all but the last four" of a
    // four-character value shows all of it.
    public static string Mask(string? value, MaskingStrategy strategy) => value switch
    {
        null => Full,
        _ => strategy switch
        {
            MaskingStrategy.Email => MaskEmail(value),
            MaskingStrategy.Phone => KeepLast(value, 2),
            MaskingStrategy.LastFour => KeepLast(value, 4),
            _ => Full,
        },
    };

    private static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        return at < 1 || at == email.Length - 1 ? Full : $"{email[0]}***{email[at..]}";
    }

    private static string KeepLast(string value, int visible) =>
        value.Length <= visible * 2 ? Full : new string('*', value.Length - visible) + value[^visible..];
}
