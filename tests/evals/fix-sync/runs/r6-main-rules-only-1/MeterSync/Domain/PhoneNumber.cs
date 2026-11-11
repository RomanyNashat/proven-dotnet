namespace MeterSync.Domain;

/// <summary>The SMS gateway wants E.164 (<c>+9665…</c>); patient records hold numbers as people type them.</summary>
public static class PhoneNumber
{
    private const string SaudiCode = "966";

    /// <summary>Normalizes a phone number to E.164, reading local numbers as Saudi.</summary>
    /// <param name="phone">The number as stored.</param>
    /// <param name="e164">The number in E.164 form when this returns <c>true</c>.</param>
    /// <returns><c>false</c> when the input isn't a phone number.</returns>
    public static bool TryToE164(string? phone, out string e164)
    {
        e164 = "";
        if (string.IsNullOrWhiteSpace(phone))
        {
            return false;
        }

        var trimmed = phone.Trim();
        var international = trimmed.StartsWith('+');
        var digits = new string(trimmed.Where(c => c is not (' ' or '-' or '(' or ')' or '.' or '+')).ToArray());
        if (digits.Length == 0 || !digits.All(char.IsAsciiDigit))
        {
            return false;
        }

        if (!international && digits.StartsWith("00", StringComparison.Ordinal))
        {
            (digits, international) = (digits[2..], true);
        }

        if (!international)
        {
            digits = digits switch
            {
                { Length: 12 } when digits.StartsWith(SaudiCode, StringComparison.Ordinal) => digits,
                { Length: 10 } when digits.StartsWith('0') => SaudiCode + digits[1..],
                { Length: 9 } => SaudiCode + digits,
                _ => "",
            };
        }

        if (digits.Length is < 8 or > 15)
        {
            return false;
        }

        e164 = "+" + digits;
        return true;
    }
}
