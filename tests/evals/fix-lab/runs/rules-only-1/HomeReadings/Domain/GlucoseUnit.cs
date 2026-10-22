namespace HomeReadings.Domain;

/// <summary>
/// The two units glucose meters display. Meters sold in Saudi Arabia show either, so a reading keeps
/// the unit the meter showed and the alert rule converts.
/// </summary>
public static class GlucoseUnit
{
    public const string MmolPerLitre = "mmol/L";
    public const string MgPerDecilitre = "mg/dL";

    /// <summary>mg/dL per mmol/L for glucose (molar mass 180.16 g/mol).</summary>
    public const decimal MgPerDlPerMmolPerL = 18.016m;

    /// <summary>Returns the canonical spelling of a unit, or null when it isn't a glucose unit.</summary>
    public static string? Normalize(string? unit) => unit?.Trim() switch
    {
        { } u when u.Equals(MmolPerLitre, StringComparison.OrdinalIgnoreCase) => MmolPerLitre,
        { } u when u.Equals(MgPerDecilitre, StringComparison.OrdinalIgnoreCase) => MgPerDecilitre,
        _ => null,
    };

    public static decimal ToMmolPerLitre(decimal value, string unit) =>
        unit == MgPerDecilitre ? value / MgPerDlPerMmolPerL : value;
}
