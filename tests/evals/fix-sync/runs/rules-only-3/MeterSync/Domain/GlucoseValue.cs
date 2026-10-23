using System.Globalization;

namespace MeterSync.Domain;

/// <summary>A blood glucose value as a meter reports it, read into mg/dL.</summary>
/// <param name="MgDl">The value in mg/dL, or null when the meter showed HI or LO (outside its range).</param>
/// <param name="AboveRange">The meter showed HI: over 600 mg/dL.</param>
/// <param name="BelowRange">The meter showed LO: under 20 mg/dL.</param>
public sealed record GlucoseValue(decimal? MgDl, bool AboveRange, bool BelowRange)
{
    public const string MgDlUnit = "mg/dL";
    public const string MmolLUnit = "mmol/L";

    // 1 mmol/L of glucose is 18 mg/dL (VENDOR.md).
    private const decimal MgDlPerMmolL = 18m;

    // Above 600 mg/dL the meter shows HI, so a larger number is a bad payload (and would overflow the column).
    private const decimal MaxPlausibleMgDl = 1000m;

    /// <summary>Reads the vendor's <c>value</c> and <c>unit</c>.</summary>
    /// <returns>The value, or null when the unit is unknown or the value isn't a reading.</returns>
    public static GlucoseValue? Parse(string? raw, string? unit)
    {
        var isMgDl = string.Equals(unit, MgDlUnit, StringComparison.OrdinalIgnoreCase);
        var isMmolL = string.Equals(unit, MmolLUnit, StringComparison.OrdinalIgnoreCase);
        if ((!isMgDl && !isMmolL) || string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var text = raw.Trim();
        if (string.Equals(text, "HI", StringComparison.OrdinalIgnoreCase))
        {
            return new GlucoseValue(null, AboveRange: true, BelowRange: false);
        }

        if (string.Equals(text, "LO", StringComparison.OrdinalIgnoreCase))
        {
            return new GlucoseValue(null, AboveRange: false, BelowRange: true);
        }

        // Only a plain decimal: NumberStyles.Number would read "5,5" as 55 (thousands separator).
        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) || value <= 0)
        {
            return null;
        }

        var mgDl = isMmolL ? value * MgDlPerMmolL : value;
        if (mgDl > MaxPlausibleMgDl)
        {
            return null;
        }

        return new GlucoseValue(Math.Round(mgDl, 1, MidpointRounding.AwayFromZero), AboveRange: false, BelowRange: false);
    }
}
