using System.Globalization;
using System.Text;

namespace HomeReadings.Application;

/// <summary>
/// Reads numbers as the app sends them: as the meter or the patient's keyboard shows them. That can be
/// "7.5", "7,5" or Arabic-Indic digits ("٧٫٥"). Parsing never depends on the server's or the request's
/// culture: ar-SA would read "7.5" differently from en-US.
/// </summary>
public static class MeterText
{
    public static bool TryParseValue(string? text, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalized = NormalizeDigits(text.Trim())
            .Replace('٫', '.') // Arabic decimal separator
            .Replace(',', '.');

        return decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Turns Arabic-Indic and Eastern Arabic-Indic digits into ASCII digits; leaves everything else.</summary>
    public static string NormalizeDigits(string text)
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
