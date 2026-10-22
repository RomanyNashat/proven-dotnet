namespace HomeReadings.Domain;

/// <summary>The two units glucose meters show. Saudi meters are commonly mg/dL, others mmol/L.</summary>
public static class GlucoseUnit
{
    public const string MmolPerLitre = "mmol/L";
    public const string MgPerDecilitre = "mg/dL";

    /// <summary>The longest canonical spelling, for the column.</summary>
    public const int MaxLength = 16;

    // 1 mmol/L of glucose is 18 mg/dL (molar mass 180 g/mol).
    private const decimal MgPerDecilitrePerMmol = 18m;

    /// <summary>Accepts either unit in any letter case and gives its canonical spelling.</summary>
    /// <param name="unit">The unit as sent.</param>
    /// <param name="canonical">"mmol/L" or "mg/dL".</param>
    /// <returns>Whether the unit is a glucose unit.</returns>
    public static bool TryNormalize(string? unit, out string canonical)
    {
        canonical = unit?.Trim() switch
        {
            { } u when u.Equals(MmolPerLitre, StringComparison.OrdinalIgnoreCase) => MmolPerLitre,
            { } u when u.Equals(MgPerDecilitre, StringComparison.OrdinalIgnoreCase) => MgPerDecilitre,
            _ => "",
        };
        return canonical.Length > 0;
    }

    /// <summary>Converts a value in a canonical unit to mmol/L.</summary>
    /// <param name="value">The value.</param>
    /// <param name="unit">Its unit, as given by <see cref="TryNormalize"/>.</param>
    /// <returns>The value in mmol/L, rounded to one decimal as meters show it.</returns>
    public static decimal ToMmolPerLitre(decimal value, string unit) =>
        unit == MgPerDecilitre ? Math.Round(value / MgPerDecilitrePerMmol, 1, MidpointRounding.AwayFromZero) : value;
}
