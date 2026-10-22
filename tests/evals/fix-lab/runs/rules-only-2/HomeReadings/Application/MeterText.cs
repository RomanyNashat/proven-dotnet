using System.Globalization;
using System.Text;

namespace HomeReadings.Application;

/// <summary>
/// Reads text as the app sends it: the meter's value as shown, and the national ID, possibly in Arabic-Indic
/// digits. Parsing is culture-invariant, so the patient's language never changes the number.
/// </summary>
public static class MeterText
{
    /// <summary>Parses "7.5", "7,5" or "٧٫٥". No sign, no thousands separator, at most two decimals.</summary>
    public static bool TryParseValue(string? text, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text) || text.Length > 16)
        {
            return false;
        }

        var normalized = ToAsciiDigits(text.Trim())
            .Replace('٫', '.') // Arabic decimal separator
            .Replace(',', '.');

        return decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value)
            && value.Scale <= 2;
    }

    /// <summary>Turns Arabic-Indic (٠-٩) and Eastern Arabic-Indic (۰-۹) digits into 0-9.</summary>
    public static string ToAsciiDigits(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            builder.Append(c switch
            {
                >= '٠' and <= '٩' => (char)('0' + (c - '٠')),
                >= '۰' and <= '۹' => (char)('0' + (c - '۰')),
                _ => c,
            });
        }

        return builder.ToString();
    }
}
