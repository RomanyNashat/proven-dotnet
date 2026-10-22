using System.Globalization;

namespace HomeReadings.Domain;

/// <summary>
/// What the meter's screen showed: a number, or HI/LO when the reading is outside its range.
/// </summary>
/// <param name="Number">The number shown; null when the meter showed HI or LO.</param>
/// <param name="Limit">HI or LO; null when the meter showed a number.</param>
public readonly record struct MeterValue(decimal? Number, MeterLimit? Limit)
{
    /// <summary>The column is numeric(6,2).</summary>
    public const decimal Largest = 9999.99m;

    /// <summary>
    /// Reads the value as the app sends it: the meter's text. Arabic-Indic digits and a comma or Arabic
    /// decimal separator are accepted, and the request's culture plays no part.
    /// </summary>
    /// <param name="text">The text the meter showed.</param>
    /// <param name="value">The reading, when the text is one.</param>
    /// <returns>Whether the text is a reading.</returns>
    public static bool TryParse(string? text, out MeterValue value)
    {
        value = default;
        var normalized = Normalize(text);
        switch (normalized.ToUpperInvariant())
        {
            case "HI":
                value = new MeterValue(null, MeterLimit.High);
                return true;
            case "LO":
                value = new MeterValue(null, MeterLimit.Low);
                return true;
        }

        if (!decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number)
            || number <= 0 || number > Largest)
        {
            return false;
        }

        value = new MeterValue(Math.Round(number, 2, MidpointRounding.AwayFromZero), null);
        return true;
    }

    // A glucose value never has a thousands separator, so a comma is the decimal separator, as on
    // meters set to a European locale.
    private static string Normalize(string? text) =>
        string.Create(text?.Trim().Length ?? 0, text?.Trim() ?? "", static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                span[i] = source[i] switch
                {
                    >= '٠' and <= '٩' and var c => (char)('0' + (c - '٠')),
                    >= '۰' and <= '۹' and var c => (char)('0' + (c - '۰')),
                    '٫' or ',' => '.',
                    var c => c,
                };
            }
        });
}
