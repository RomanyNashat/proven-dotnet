namespace HomeReadings.Domain;

/// <summary>The units a glucose meter shows. Readings are stored in the unit the meter showed.</summary>
public static class GlucoseUnit
{
    public const string MmolPerLitre = "mmol/L";
    public const string MgPerDecilitre = "mg/dL";

    /// <summary>Accepts either unit in any letter case and returns its canonical name.</summary>
    /// <param name="text">The unit as the app sent it.</param>
    /// <param name="unit">The canonical name, when the unit is known.</param>
    /// <returns>Whether the unit is one this service understands.</returns>
    public static bool TryNormalize(string? text, out string unit)
    {
        var trimmed = text?.Trim();
        unit = trimmed switch
        {
            _ when string.Equals(trimmed, MmolPerLitre, StringComparison.OrdinalIgnoreCase) => MmolPerLitre,
            _ when string.Equals(trimmed, MgPerDecilitre, StringComparison.OrdinalIgnoreCase) => MgPerDecilitre,
            _ => "",
        };
        return unit.Length > 0;
    }
}
