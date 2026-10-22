using System.Globalization;

namespace HomeReadings.Domain;

/// <summary>
/// The value as the meter shows it, sent as text. It is read the same way whatever the caller's language:
/// a culture-sensitive parse turns "11.5" into 115 under a comma-decimal culture, or fails under ar-SA
/// on some ICU builds.
/// </summary>
public static class MeterValue
{
    private const int MaxLength = 16;

    /// <summary>Reads a plain decimal number. Arabic-Indic digits and the Arabic or comma decimal separator are accepted.</summary>
    /// <param name="text">The text the app sent.</param>
    /// <param name="value">The number, when the text is one.</param>
    /// <returns><c>true</c> when the text is a plain, unsigned decimal number.</returns>
    public static bool TryParse(string? text, out decimal value)
    {
        value = 0;
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxLength)
        {
            return false;
        }

        Span<char> normalized = stackalloc char[trimmed.Length];
        for (var i = 0; i < trimmed.Length; i++)
        {
            normalized[i] = Normalize(trimmed[i]);
        }

        return decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>A meter shows HI or LO when the glucose is outside what it can measure: both need urgent care.</summary>
    /// <param name="text">The text the app sent.</param>
    /// <returns><c>true</c> for HI or LO, in any case.</returns>
    public static bool IsOutOfMeterRange(string? text) =>
        text?.Trim() is { } t && (t.Equals("HI", StringComparison.OrdinalIgnoreCase) || t.Equals("LO", StringComparison.OrdinalIgnoreCase));

    private static char Normalize(char c) => c switch
    {
        >= '٠' and <= '٩' => (char)('0' + (c - '٠')),   // Arabic-Indic digits
        >= '۰' and <= '۹' => (char)('0' + (c - '۰')),   // Extended Arabic-Indic digits
        '٫' or ',' => '.',                                         // Arabic decimal separator, comma
        _ => c,
    };
}
