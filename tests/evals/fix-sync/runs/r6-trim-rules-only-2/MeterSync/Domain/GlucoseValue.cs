using System.Globalization;

namespace MeterSync.Domain;

/// <summary>A meter's displayed value, interpreted in mg/dL (see VENDOR.md).</summary>
public sealed record GlucoseValue(string Raw, string Unit, decimal? MgDl, AlertKind Alert)
{
    // The care team's thresholds, in mg/dL.
    public const decimal LowBelow = 70m;
    public const decimal HighAbove = 250m;

    public const string MgDlUnit = "mg/dL";
    public const string MmolUnit = "mmol/L";

    private const decimal MgDlPerMmol = 18m;

    /// <summary>Interprets what the meter showed.</summary>
    /// <param name="raw">The vendor's <c>value</c>: a number, <c>HI</c> or <c>LO</c>.</param>
    /// <param name="unit">The vendor's <c>unit</c>.</param>
    /// <returns>The value, or null when it can't be interpreted.</returns>
    public static GlucoseValue? TryParse(string? raw, string? unit)
    {
        var normalisedUnit = unit switch
        {
            _ when string.Equals(unit, MgDlUnit, StringComparison.OrdinalIgnoreCase) => MgDlUnit,
            _ when string.Equals(unit, MmolUnit, StringComparison.OrdinalIgnoreCase) => MmolUnit,
            _ => null,
        };
        if (normalisedUnit is null || string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var trimmed = raw.Trim();

        // HI and LO are the meter's own out-of-range readings: the most urgent ones, never skipped.
        if (string.Equals(trimmed, "HI", StringComparison.OrdinalIgnoreCase))
        {
            return new GlucoseValue("HI", normalisedUnit, null, AlertKind.High);
        }

        if (string.Equals(trimmed, "LO", StringComparison.OrdinalIgnoreCase))
        {
            return new GlucoseValue("LO", normalisedUnit, null, AlertKind.Low);
        }

        // Decimal point only: NumberStyles.Number would read "5,5" as 55.
        if (!decimal.TryParse(trimmed, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
            || value <= 0m || trimmed.Length > 16)
        {
            return null;
        }

        var mgDl = Math.Round(normalisedUnit == MmolUnit ? value * MgDlPerMmol : value, 1, MidpointRounding.AwayFromZero);
        if (mgDl >= 10_000m)
        {
            return null;
        }

        var alert = mgDl < LowBelow ? AlertKind.Low : mgDl > HighAbove ? AlertKind.High : AlertKind.None;
        return new GlucoseValue(trimmed, normalisedUnit, mgDl, alert);
    }
}
