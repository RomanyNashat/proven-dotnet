using System.Diagnostics.CodeAnalysis;

namespace LabResults.Application;

/// <summary>Saudi mobile numbers, the only numbers the service sends SMS to.</summary>
public static class SaudiMobile
{
    private const string CountryPrefix = "+966";

    /// <summary>
    /// Accepts <c>+9665XXXXXXXX</c>, <c>009665XXXXXXXX</c>, <c>05XXXXXXXX</c> or <c>5XXXXXXXX</c>, in ASCII or
    /// Arabic-Indic digits, and returns it as <c>+9665XXXXXXXX</c>.
    /// </summary>
    /// <param name="input">The number as typed.</param>
    /// <param name="normalized">The E.164 number, when valid.</param>
    /// <returns>Whether <paramref name="input"/> is a Saudi mobile number.</returns>
    public static bool TryNormalize(string? input, [NotNullWhen(true)] out string? normalized)
    {
        normalized = null;
        if (input is null)
        {
            return false;
        }

        var ascii = Digits.ToAscii(input.Trim());
        var national = ascii switch
        {
            _ when ascii.StartsWith(CountryPrefix, StringComparison.Ordinal) => ascii[CountryPrefix.Length..],
            _ when ascii.StartsWith("00966", StringComparison.Ordinal) => ascii[5..],
            _ when ascii.StartsWith('0') => ascii[1..],
            _ => ascii,
        };

        if (national.Length != 9 || national[0] != '5' || !Digits.AreAllAscii(national))
        {
            return false;
        }

        normalized = CountryPrefix + national;
        return true;
    }
}
