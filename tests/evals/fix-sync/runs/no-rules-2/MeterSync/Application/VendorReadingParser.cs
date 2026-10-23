using System.Globalization;
using MeterSync.Infrastructure;

namespace MeterSync.Application;

/// <summary>Turns a vendor reading (VENDOR.md) into the mg/dL value we store and compare against the thresholds.</summary>
public static class VendorReadingParser
{
    // The meter shows HI above 600 mg/dL and LO below 20 mg/dL; the true value is unknown, so store a value just past the limit.
    public const decimal HiMgDl = 601m;
    public const decimal LoMgDl = 19m;

    private const decimal MmolToMgDl = 18m;
    private const decimal MaxMgDl = 5000m;

    public static bool TryParse(VendorReading reading, out decimal mgDl)
    {
        mgDl = 0;
        var raw = reading.Value?.Trim();
        var unit = reading.Unit?.Trim();
        if (string.IsNullOrEmpty(raw) || string.IsNullOrEmpty(unit))
        {
            return false;
        }

        if (raw.Equals("HI", StringComparison.OrdinalIgnoreCase))
        {
            mgDl = HiMgDl;
            return true;
        }

        if (raw.Equals("LO", StringComparison.OrdinalIgnoreCase))
        {
            mgDl = LoMgDl;
            return true;
        }

        if (!decimal.TryParse(raw, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) || value <= 0)
        {
            return false;
        }

        if (unit.Equals("mmol/L", StringComparison.OrdinalIgnoreCase))
        {
            value *= MmolToMgDl;
        }
        else if (!unit.Equals("mg/dL", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (value > MaxMgDl)
        {
            return false;
        }

        mgDl = Math.Round(value, 1, MidpointRounding.AwayFromZero);
        return true;
    }
}
