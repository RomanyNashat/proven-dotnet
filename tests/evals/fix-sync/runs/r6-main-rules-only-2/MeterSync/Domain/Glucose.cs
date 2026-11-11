using System.Globalization;

namespace MeterSync.Domain;

/// <summary>A blood glucose value as a meter reported it, normalised to mg/dL.</summary>
/// <param name="MgDl">The value in mg/dL, or null when the meter showed HI or LO.</param>
/// <param name="Unit">The unit the meter showed, in its canonical spelling.</param>
/// <param name="AboveRange">The meter showed HI: over 600 mg/dL.</param>
/// <param name="BelowRange">The meter showed LO: under 20 mg/dL.</param>
public sealed record Glucose(decimal? MgDl, string Unit, bool AboveRange, bool BelowRange)
{
    public const string MgDlUnit = "mg/dL";
    public const string MmolLUnit = "mmol/L";

    // 1 mmol/L is 18 mg/dL (VENDOR.md).
    private const decimal MgDlPerMmolL = 18m;

    // Meters report at most 600 mg/dL (33.3 mmol/L); anything far past that is a corrupt value.
    private const decimal MaxPlausibleMgDl = 1000m;

    /// <summary>Parses the vendor's <c>value</c> and <c>unit</c> fields.</summary>
    /// <returns>False when the value or unit isn't one the vendor documents.</returns>
    public static bool TryParse(string? raw, string? unit, out Glucose glucose)
    {
        glucose = null!;
        var canonicalUnit = unit switch
        {
            _ when string.Equals(unit, MgDlUnit, StringComparison.OrdinalIgnoreCase) => MgDlUnit,
            _ when string.Equals(unit, MmolLUnit, StringComparison.OrdinalIgnoreCase) => MmolLUnit,
            _ => null,
        };
        if (canonicalUnit is null || string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var trimmed = raw.Trim();
        if (string.Equals(trimmed, "HI", StringComparison.OrdinalIgnoreCase))
        {
            glucose = new Glucose(null, canonicalUnit, AboveRange: true, BelowRange: false);
            return true;
        }

        if (string.Equals(trimmed, "LO", StringComparison.OrdinalIgnoreCase))
        {
            glucose = new Glucose(null, canonicalUnit, AboveRange: false, BelowRange: true);
            return true;
        }

        // Decimal point only: NumberStyles.Number would also accept thousands separators, so "5,5" would read as 55.
        if (!decimal.TryParse(trimmed, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) || value <= 0)
        {
            return false;
        }

        var mgDl = Math.Round(canonicalUnit == MmolLUnit ? value * MgDlPerMmolL : value, 1, MidpointRounding.AwayFromZero);
        if (mgDl > MaxPlausibleMgDl)
        {
            return false;
        }

        glucose = new Glucose(mgDl, canonicalUnit, AboveRange: false, BelowRange: false);
        return true;
    }

    /// <summary>Classifies the value against the care team's thresholds.</summary>
    public AlertKind Classify(decimal lowBelowMgDl, decimal highAboveMgDl) => this switch
    {
        { BelowRange: true } => AlertKind.Low,
        { AboveRange: true } => AlertKind.High,
        { MgDl: { } v } when v < lowBelowMgDl => AlertKind.Low,
        { MgDl: { } v } when v > highAboveMgDl => AlertKind.High,
        _ => AlertKind.None,
    };
}
