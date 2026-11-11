using System.Globalization;

namespace MeterSync.Domain;

/// <summary>A meter reading understood: its value in mg/dL (null when the meter showed HI or LO) and its alert.</summary>
public sealed record GlucoseLevel(decimal? MgDl, AlertKind Alert)
{
    // 1 mmol/L is 18 mg/dL (VENDOR.md).
    private const decimal MgDlPerMmolL = 18m;

    // Anything outside this can't come from a glucose meter; storing it would overflow numeric(6,1) or alert on garbage.
    private const decimal PlausibleMaxMgDl = 1000m;

    /// <summary>Reads the value and unit as the vendor sends them.</summary>
    /// <param name="raw">The value as the meter showed it: a number, <c>HI</c> or <c>LO</c>.</param>
    /// <param name="unit"><c>mg/dL</c> or <c>mmol/L</c>.</param>
    /// <returns>The level, or null when the value or unit can't be understood.</returns>
    public static GlucoseLevel? Parse(string? raw, string? unit)
    {
        var value = raw?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        // HI and LO are the meter's off-scale readings: the most urgent ones, never to be dropped.
        if (value.Equals("HI", StringComparison.OrdinalIgnoreCase))
        {
            return new GlucoseLevel(null, AlertKind.High);
        }

        if (value.Equals("LO", StringComparison.OrdinalIgnoreCase))
        {
            return new GlucoseLevel(null, AlertKind.Low);
        }

        decimal? factor = unit?.Trim() switch
        {
            { } u when u.Equals("mg/dL", StringComparison.OrdinalIgnoreCase) => 1m,
            { } u when u.Equals("mmol/L", StringComparison.OrdinalIgnoreCase) => MgDlPerMmolL,
            _ => null,
        };

        // AllowDecimalPoint only: NumberStyles.Number would read "1,2" as 12.
        if (factor is null
            || !decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
        {
            return null;
        }

        var mgDl = Math.Round(number * factor.Value, 1, MidpointRounding.AwayFromZero);
        if (mgDl <= 0 || mgDl > PlausibleMaxMgDl)
        {
            return null;
        }

        var alert = mgDl < Reading.LowBelow ? AlertKind.Low
            : mgDl > Reading.HighAbove ? AlertKind.High
            : AlertKind.None;
        return new GlucoseLevel(mgDl, alert);
    }
}
