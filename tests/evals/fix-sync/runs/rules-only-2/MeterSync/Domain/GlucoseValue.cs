using System.Globalization;

namespace MeterSync.Domain;

/// <summary>Turns what the meter's screen showed into mg/dL (see VENDOR.md).</summary>
public static class GlucoseValue
{
    // The meter's range, in mg/dL. It shows HI above it and LO below it.
    public const decimal MeterMax = 600m;
    public const decimal MeterMin = 20m;

    private const decimal MgDlPerMmolL = 18m;

    /// <summary>
    /// Reads a vendor value. HI and LO are the most urgent readings a meter can show, so they map to the
    /// edge of the meter's range (and so out of the care team's range) instead of being dropped.
    /// </summary>
    /// <param name="raw">The value as the meter showed it: a number, <c>HI</c> or <c>LO</c>.</param>
    /// <param name="unit"><c>mg/dL</c> or <c>mmol/L</c>.</param>
    /// <param name="mgDl">The value in mg/dL, rounded to one decimal.</param>
    /// <returns><c>false</c> when the value or the unit isn't one the vendor documents.</returns>
    public static bool TryToMgDl(string? raw, string? unit, out decimal mgDl)
    {
        mgDl = 0;
        if (string.IsNullOrWhiteSpace(raw) || string.IsNullOrWhiteSpace(unit))
        {
            return false;
        }

        var isMgDl = string.Equals(unit, "mg/dL", StringComparison.OrdinalIgnoreCase);
        var isMmolL = string.Equals(unit, "mmol/L", StringComparison.OrdinalIgnoreCase);
        if (!isMgDl && !isMmolL)
        {
            return false;
        }

        var value = raw.Trim();
        if (string.Equals(value, "HI", StringComparison.OrdinalIgnoreCase))
        {
            mgDl = MeterMax;
            return true;
        }

        if (string.Equals(value, "LO", StringComparison.OrdinalIgnoreCase))
        {
            mgDl = MeterMin;
            return true;
        }

        if (!decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number) || number <= 0)
        {
            return false;
        }

        var converted = isMmolL ? number * MgDlPerMmolL : number;

        // Anything past this isn't a reading a meter can show; it would also overflow numeric(6,1).
        if (converted > 9999m)
        {
            return false;
        }

        mgDl = Math.Round(converted, 1, MidpointRounding.AwayFromZero);
        return true;
    }
}
