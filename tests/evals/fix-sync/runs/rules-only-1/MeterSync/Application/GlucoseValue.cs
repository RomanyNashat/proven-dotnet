using System.Globalization;

namespace MeterSync.Application;

/// <summary>Turns a vendor reading's value and unit into mg/dL (see VENDOR.md).</summary>
public static class GlucoseValue
{
    /// <summary>What the meter shows above its range.</summary>
    public const string High = "HI";

    /// <summary>What the meter shows below its range.</summary>
    public const string Low = "LO";

    public const string MgDl = "mg/dL";
    public const string MmolL = "mmol/L";

    /// <summary>The top of the meter's range; <c>HI</c> means above it.</summary>
    public const decimal MeterMaxMgDl = 600m;

    /// <summary>The bottom of the meter's range; <c>LO</c> means below it.</summary>
    public const decimal MeterMinMgDl = 20m;

    public const decimal MgDlPerMmolL = 18m;

    // Anything outside this isn't a glucose reading (and wouldn't fit numeric(6,1)).
    private const decimal PlausibleMaxMgDl = 1000m;

    /// <summary>Normalises the raw value and unit and converts the value to mg/dL.</summary>
    /// <param name="value">The value as the vendor sent it.</param>
    /// <param name="unit">The unit as the vendor sent it.</param>
    /// <param name="parsed">The normalised raw value, unit and mg/dL value.</param>
    /// <returns><c>false</c> when the value or unit isn't one the vendor documents.</returns>
    public static bool TryParse(string? value, string? unit, out (string Raw, string Unit, decimal MgDl) parsed)
    {
        parsed = default;
        var raw = value?.Trim() ?? "";
        var normalisedUnit = NormaliseUnit(unit);
        if (normalisedUnit is null)
        {
            return false;
        }

        // HI and LO are the readings that matter most: above 600 or below 20 mg/dL. They are stored at the
        // range limit, which is past the alert threshold in the right direction.
        if (raw.Equals(High, StringComparison.OrdinalIgnoreCase))
        {
            parsed = (High, normalisedUnit, MeterMaxMgDl);
            return true;
        }

        if (raw.Equals(Low, StringComparison.OrdinalIgnoreCase))
        {
            parsed = (Low, normalisedUnit, MeterMinMgDl);
            return true;
        }

        if (raw.Length > 16
            || !decimal.TryParse(raw, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
        {
            return false;
        }

        var mgDl = normalisedUnit == MmolL
            ? Math.Round(number * MgDlPerMmolL, 1, MidpointRounding.AwayFromZero)
            : Math.Round(number, 1, MidpointRounding.AwayFromZero);
        if (mgDl <= 0 || mgDl > PlausibleMaxMgDl)
        {
            return false;
        }

        parsed = (raw, normalisedUnit, mgDl);
        return true;
    }

    private static string? NormaliseUnit(string? unit) => unit?.Trim() switch
    {
        { } u when u.Equals(MgDl, StringComparison.OrdinalIgnoreCase) => MgDl,
        { } u when u.Equals(MmolL, StringComparison.OrdinalIgnoreCase) => MmolL,
        _ => null,
    };
}
