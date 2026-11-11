using System.Globalization;

namespace MeterSync.Domain;

public enum AlertKind
{
    None,
    Low,
    High,
}

public sealed class Reading
{
    // The care team's thresholds, in mg/dL.
    public const decimal LowBelow = 70m;
    public const decimal HighAbove = 250m;

    public const int VendorIdMaxLength = 64;
    public const int RawMaxLength = 16;
    public const int UnitMaxLength = 8;

    // The meter shows HI above 600 mg/dL and LO below 20 mg/dL (VENDOR.md); store the range limit.
    private const decimal MeterHigh = 600m;
    private const decimal MeterLow = 20m;
    private const decimal MgDlPerMmolL = 18m;

    // ValueMgDl is numeric(6,1).
    private const decimal MaxStorable = 99999.9m;

    private Reading()
    {
    }

    public int Id { get; private set; }

    public int PatientId { get; private set; }

    public int MeterId { get; private set; }

    public string VendorId { get; private set; } = "";

    /// <summary>The value as the meter showed it.</summary>
    public string Raw { get; private set; } = "";

    public string Unit { get; private set; } = "";

    public decimal ValueMgDl { get; private set; }

    public DateTimeOffset TakenAt { get; private set; }

    public AlertKind Alert { get; private set; }

    public DateTimeOffset? AlertSentAt { get; private set; }

    /// <summary>Failed attempts to text the patient. Only the alert sweeper's update statement writes it.</summary>
    public int AlertFailures { get; private set; }

    /// <summary>Builds a reading from what the vendor's API returned.</summary>
    /// <param name="patientId">The meter's patient.</param>
    /// <param name="meterId">The meter.</param>
    /// <param name="vendorId">The vendor's reading id.</param>
    /// <param name="value">A number, <c>HI</c> or <c>LO</c>, as the meter showed it.</param>
    /// <param name="unit"><c>mg/dL</c> or <c>mmol/L</c>.</param>
    /// <param name="takenAt">When the reading was taken.</param>
    /// <returns>The reading, or <see langword="null"/> when it can't be stored (unknown unit, unreadable value, too long).</returns>
    public static Reading? FromVendor(int patientId, int meterId, string? vendorId, string? value, string? unit, DateTimeOffset takenAt)
    {
        if (!Fits(vendorId, VendorIdMaxLength) || !Fits(value, RawMaxLength) || !Fits(unit, UnitMaxLength))
        {
            return null;
        }

        if (ToMgDl(value!, unit!) is not { } mgDl)
        {
            return null;
        }

        return new Reading
        {
            PatientId = patientId,
            MeterId = meterId,
            VendorId = vendorId!,
            Raw = value!,
            Unit = unit!,
            ValueMgDl = mgDl,
            TakenAt = takenAt.ToUniversalTime(),
            Alert = mgDl < LowBelow ? AlertKind.Low : mgDl > HighAbove ? AlertKind.High : AlertKind.None,
        };
    }

    public void MarkAlertSent(DateTimeOffset at) => AlertSentAt = at;

    private static bool Fits(string? text, int maxLength) => !string.IsNullOrWhiteSpace(text) && text.Length <= maxLength;

    private static decimal? ToMgDl(string value, string unit)
    {
        decimal? factor = unit switch
        {
            _ when unit.Equals("mg/dL", StringComparison.OrdinalIgnoreCase) => 1m,
            _ when unit.Equals("mmol/L", StringComparison.OrdinalIgnoreCase) => MgDlPerMmolL,
            _ => null,
        };
        if (factor is null)
        {
            return null;
        }

        if (value.Equals("HI", StringComparison.OrdinalIgnoreCase))
        {
            return MeterHigh;
        }

        if (value.Equals("LO", StringComparison.OrdinalIgnoreCase))
        {
            return MeterLow;
        }

        if (!decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
        {
            return null;
        }

        var mgDl = Math.Round(number * factor.Value, 1, MidpointRounding.AwayFromZero);
        return mgDl <= MaxStorable ? mgDl : null;
    }
}
