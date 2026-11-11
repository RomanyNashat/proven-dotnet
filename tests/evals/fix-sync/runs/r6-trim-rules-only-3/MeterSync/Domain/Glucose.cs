using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace MeterSync.Domain;

/// <summary>Turns a reading as the meter shows it (VENDOR.md) into mg/dL.</summary>
public static class Glucose
{
    /// <summary>The meter shows <c>HI</c> above this, in mg/dL.</summary>
    public const decimal MeterMax = 600m;

    /// <summary>The meter shows <c>LO</c> below this, in mg/dL.</summary>
    public const decimal MeterMin = 20m;

    public const string MgDl = "mg/dL";
    public const string MmolL = "mmol/L";

    // 1 mmol/L of glucose is 18 mg/dL (VENDOR.md).
    private const decimal MgDlPerMmolL = 18m;

    // Anything above this is not a glucose reading but a corrupt value; it also keeps numeric(6,1) safe.
    private const decimal Implausible = 2000m;

    /// <summary>Converts the meter's value to mg/dL.</summary>
    /// <param name="raw">The value as shown: a number, <c>HI</c> or <c>LO</c>.</param>
    /// <param name="unit"><c>mg/dL</c> or <c>mmol/L</c>.</param>
    /// <param name="mgDl">The value in mg/dL; for HI and LO, the edge of the meter's range.</param>
    /// <returns>False when the value or unit can't be understood.</returns>
    public static bool TryToMgDl([NotNullWhen(true)] string? raw, [NotNullWhen(true)] string? unit, out decimal mgDl)
    {
        mgDl = 0m;
        var factor = unit switch
        {
            _ when string.Equals(unit, MgDl, StringComparison.OrdinalIgnoreCase) => 1m,
            _ when string.Equals(unit, MmolL, StringComparison.OrdinalIgnoreCase) => MgDlPerMmolL,
            _ => 0m,
        };
        if (factor == 0m || string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        // HI and LO are the readings that matter most: they are beyond the meter's range, not errors.
        if (string.Equals(raw.Trim(), "HI", StringComparison.OrdinalIgnoreCase))
        {
            mgDl = MeterMax;
            return true;
        }

        if (string.Equals(raw.Trim(), "LO", StringComparison.OrdinalIgnoreCase))
        {
            mgDl = MeterMin;
            return true;
        }

        if (!decimal.TryParse(raw, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite,
                CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        var converted = Math.Round(value * factor, 1, MidpointRounding.AwayFromZero);
        if (converted <= 0m || converted > Implausible)
        {
            return false;
        }

        mgDl = converted;
        return true;
    }
}
