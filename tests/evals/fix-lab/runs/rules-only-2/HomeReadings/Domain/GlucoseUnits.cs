namespace HomeReadings.Domain;

/// <summary>The two units glucose meters show. The alert level is in mmol/L, so mg/dL is converted before comparing.</summary>
public static class GlucoseUnits
{
    public const string MmolPerL = "mmol/L";
    public const string MgPerDl = "mg/dL";

    // Glucose's molar mass is 180.16 g/mol, so 1 mmol/L is 18.016 mg/dL.
    private const decimal MgPerDlInOneMmolPerL = 18.016m;

    /// <summary>Accepts the unit in any letter case and returns its canonical spelling.</summary>
    public static bool TryNormalize(string? unit, out string canonical)
    {
        var trimmed = unit?.Trim();
        if (string.Equals(trimmed, MmolPerL, StringComparison.OrdinalIgnoreCase))
        {
            canonical = MmolPerL;
            return true;
        }

        if (string.Equals(trimmed, MgPerDl, StringComparison.OrdinalIgnoreCase))
        {
            canonical = MgPerDl;
            return true;
        }

        canonical = "";
        return false;
    }

    public static decimal ToMmolPerL(decimal value, string unit) => unit switch
    {
        MmolPerL => value,
        MgPerDl => Math.Round(value / MgPerDlInOneMmolPerL, 2, MidpointRounding.AwayFromZero),
        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, "Unknown glucose unit."),
    };

    /// <summary>The largest value a meter can show in this unit; anything above is a typing or parsing error.</summary>
    public static decimal MaxValue(string unit) => unit == MgPerDl ? 1100m : 60m;
}
