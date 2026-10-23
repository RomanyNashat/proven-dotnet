using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace MeterSync.Domain;

/// <summary>A blood glucose value in mg/dL, read from what the meter's screen shows (see VENDOR.md).</summary>
public readonly record struct Glucose(decimal MgDl)
{
    public const string MgPerDl = "mg/dL";
    public const string MmolPerL = "mmol/L";

    // The meter shows HI above 600 mg/dL and LO below 20 mg/dL, whatever unit it is set to. The bound is
    // stored so the alert logic and the numeric column work unchanged; Raw keeps what the meter showed.
    public static readonly Glucose AboveMeterRange = new(600m);
    public static readonly Glucose BelowMeterRange = new(20m);

    private const decimal MgDlPerMmolL = 18m;

    // numeric(6,1) holds up to 99999.9; anything near that is not a reading.
    private const decimal Largest = 9999.9m;

    /// <summary>Parses the meter's value and unit; false for an unknown unit or a value that isn't a glucose reading.</summary>
    public static bool TryParse(string? raw, string? unit, [NotNullWhen(true)] out string? canonicalUnit, out Glucose glucose)
    {
        glucose = default;
        canonicalUnit = Canonical(unit);
        if (canonicalUnit is null || string.IsNullOrWhiteSpace(raw))
        {
            canonicalUnit = null;
            return false;
        }

        if (string.Equals(raw, "HI", StringComparison.OrdinalIgnoreCase))
        {
            glucose = AboveMeterRange;
            return true;
        }

        if (string.Equals(raw, "LO", StringComparison.OrdinalIgnoreCase))
        {
            glucose = BelowMeterRange;
            return true;
        }

        if (!decimal.TryParse(raw, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
        {
            canonicalUnit = null;
            return false;
        }

        var mgDl = Math.Round(canonicalUnit == MmolPerL ? value * MgDlPerMmolL : value, 1, MidpointRounding.AwayFromZero);
        if (mgDl <= 0 || mgDl > Largest)
        {
            canonicalUnit = null;
            return false;
        }

        glucose = new Glucose(mgDl);
        return true;
    }

    private static string? Canonical(string? unit) =>
        string.Equals(unit, MgPerDl, StringComparison.OrdinalIgnoreCase) ? MgPerDl
        : string.Equals(unit, MmolPerL, StringComparison.OrdinalIgnoreCase) ? MmolPerL
        : null;
}
