using System.Diagnostics.CodeAnalysis;

namespace HomeReadings.Domain;

/// <summary>The units a glucose meter reports in. Anything else is refused, because the alert level depends on it.</summary>
public static class GlucoseUnits
{
    public const string MmolPerLitre = "mmol/L";
    public const string MgPerDecilitre = "mg/dL";

    private const decimal MgPerDecilitrePerMmol = 18.0182m;

    /// <summary>Matches a unit case-insensitively and returns its canonical spelling.</summary>
    /// <param name="unit">The unit the app sent.</param>
    /// <param name="canonical">The canonical spelling, when the unit is known.</param>
    /// <returns><c>true</c> for mmol/L or mg/dL.</returns>
    public static bool TryNormalize(string? unit, [NotNullWhen(true)] out string? canonical)
    {
        var trimmed = unit?.Trim();
        canonical = string.Equals(trimmed, MmolPerLitre, StringComparison.OrdinalIgnoreCase) ? MmolPerLitre
            : string.Equals(trimmed, MgPerDecilitre, StringComparison.OrdinalIgnoreCase) ? MgPerDecilitre
            : null;
        return canonical is not null;
    }

    /// <summary>Whether a stored unit is mg/dL. Rows stored before units were checked are treated as mmol/L, as they were then.</summary>
    /// <param name="unit">The stored unit.</param>
    /// <returns><c>true</c> for mg/dL.</returns>
    public static bool IsMgPerDecilitre(string? unit) =>
        string.Equals(unit?.Trim(), MgPerDecilitre, StringComparison.OrdinalIgnoreCase);

    /// <summary>The value in mmol/L, the unit the care team works in, to one decimal place.</summary>
    /// <param name="value">The value in its own unit.</param>
    /// <param name="unit">The stored unit.</param>
    /// <returns>The value in mmol/L.</returns>
    public static decimal ToMmolPerLitre(decimal value, string unit) =>
        IsMgPerDecilitre(unit) ? Math.Round(value / MgPerDecilitrePerMmol, 1, MidpointRounding.AwayFromZero) : value;
}
