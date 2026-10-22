using System.Globalization;

namespace HomeReadings.Domain;

/// <summary>
/// The value a meter showed. A meter shows <c>HI</c> or <c>LO</c> outside the range it can measure; those
/// are stored at the limit of the usual range, with the flag kept beside the value.
/// </summary>
/// <param name="Value">The number, in the reading's unit.</param>
/// <param name="OutOfRange"><c>HI</c>, <c>LO</c>, or null for a measured value.</param>
public readonly record struct MeterValue(decimal Value, string? OutOfRange)
{
    public const string High = "HI";
    public const string Low = "LO";

    // The column is numeric(6,2).
    private const decimal Largest = 9999.99m;

    /// <summary>
    /// Reads the text the meter showed. The decimal point is '.' or the Arabic '٫' whatever the request's
    /// language; a ',' is refused, because "12,5" is 12.5 on one phone and 125 on another.
    /// </summary>
    /// <param name="text">The value as the meter shows it.</param>
    /// <param name="unit">The canonical unit (<see cref="GlucoseUnit"/>).</param>
    /// <param name="value">The value read.</param>
    /// <returns>Whether the text is a value this service can store.</returns>
    public static bool TryParse(string? text, string unit, out MeterValue value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text) || unit is not (GlucoseUnit.MmolPerLitre or GlucoseUnit.MgPerDecilitre))
        {
            return false;
        }

        var mgPerDecilitre = unit == GlucoseUnit.MgPerDecilitre;
        var trimmed = Digits.ToAscii(text.Trim());
        if (string.Equals(trimmed, High, StringComparison.OrdinalIgnoreCase))
        {
            value = new MeterValue(mgPerDecilitre ? 600m : 33.3m, High);
            return true;
        }

        if (string.Equals(trimmed, Low, StringComparison.OrdinalIgnoreCase))
        {
            value = new MeterValue(mgPerDecilitre ? 20m : 1.1m, Low);
            return true;
        }

        if (!trimmed.All(c => char.IsAsciiDigit(c) || c == '.')
            || !decimal.TryParse(trimmed, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number)
            || number <= 0m || number > Largest)
        {
            return false;
        }

        value = new MeterValue(number, null);
        return true;
    }
}
