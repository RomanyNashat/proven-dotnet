using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace LabResults.Application;

/// <summary>
/// A Saudi mobile number. Only these are accepted: the SMS gateway is paid per message, and an open
/// destination lets any token holder send to any number in the world (SMS pumping).
/// </summary>
public static partial class SaudiMobile
{
    /// <summary>Accepts +9665XXXXXXXX, 009665XXXXXXXX, 05XXXXXXXX or 5XXXXXXXX, in Western or Arabic-Indic digits.</summary>
    /// <param name="value">What the caller sent.</param>
    /// <param name="e164">The number as +9665XXXXXXXX, when valid.</param>
    /// <returns>Whether the value is a Saudi mobile number.</returns>
    public static bool TryNormalize(string? value, [NotNullWhen(true)] out string? e164)
    {
        e164 = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var match = Shape().Match(Digits.ToWestern(value.Trim()));
        if (!match.Success)
        {
            return false;
        }

        e164 = "+966" + match.Groups["subscriber"].Value;
        return true;
    }

    /// <summary>The E.164 form of a value already validated.</summary>
    /// <param name="value">A value that passed <see cref="TryNormalize"/>.</param>
    /// <returns>The number as +9665XXXXXXXX.</returns>
    /// <exception cref="ArgumentException">The value is not a Saudi mobile number.</exception>
    public static string Normalize(string value) =>
        TryNormalize(value, out var e164) ? e164 : throw new ArgumentException("Not a Saudi mobile number.", nameof(value));

    [GeneratedRegex("^(?:\\+966|00966|0)?(?<subscriber>5[0-9]{8})$")]
    private static partial Regex Shape();
}
