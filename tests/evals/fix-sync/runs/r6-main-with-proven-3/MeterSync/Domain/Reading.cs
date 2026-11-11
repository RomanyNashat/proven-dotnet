using System.Globalization;

namespace MeterSync.Domain;

public sealed class Reading
{
    // The care team's thresholds, in mg/dL.
    public const decimal LowBelow = 70m;
    public const decimal HighAbove = 250m;

    // What the meter's HI / LO mean (VENDOR.md): above 600 mg/dL / below 20 mg/dL.
    // We store the range edge so the reading still sorts and alerts as the extreme it is.
    public const decimal HiMgDl = 600m;
    public const decimal LoMgDl = 20m;

    public const string MgDl = "mg/dL";
    public const string MmolL = "mmol/L";

    public const int MaxRawLength = 16;
    public const int MaxVendorIdLength = 64;

    private const decimal MgDlPerMmolL = 18m;

    // numeric(6,1)
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

    /// <summary>When a sweeper instance claimed this alert for sending (a lease; see AlertSweeper).</summary>
    public DateTimeOffset? AlertClaimedAt { get; private set; }

    public bool IsAboveMeterRange => string.Equals(Raw, "HI", StringComparison.OrdinalIgnoreCase);

    public bool IsBelowMeterRange => string.Equals(Raw, "LO", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Builds a reading from what the vendor sent, converting it to mg/dL.
    /// </summary>
    /// <returns>The reading, or <c>null</c> when the vendor's value, unit or id can't be trusted.</returns>
    public static Reading? TryFromMeter(int patientId, int meterId, string vendorId, string raw, string unit, DateTimeOffset takenAt)
    {
        if (string.IsNullOrEmpty(vendorId) || vendorId.Length > MaxVendorIdLength
            || string.IsNullOrEmpty(raw) || raw.Length > MaxRawLength
            || CanonicalUnit(unit) is not { } canonicalUnit)
        {
            return null;
        }

        var (value, alert) = raw.ToUpperInvariant() switch
        {
            "HI" => (HiMgDl, AlertKind.High),
            "LO" => (LoMgDl, AlertKind.Low),
            _ => ToMgDl(raw, canonicalUnit) is { } mgDl ? (mgDl, AlertFor(mgDl)) : ((decimal?)null, AlertKind.None),
        };

        return value is null ? null : new Reading
        {
            PatientId = patientId,
            MeterId = meterId,
            VendorId = vendorId,
            Raw = raw,
            Unit = canonicalUnit,
            ValueMgDl = value.Value,
            TakenAt = takenAt.ToUniversalTime(),
            Alert = alert,
        };
    }

    /// <summary>Records that the patient was texted about this reading, and ends the sweeper's claim.</summary>
    /// <param name="sentAt">When the gateway accepted the message.</param>
    /// <exception cref="InvalidOperationException">The reading is in range, so there is no alert to send.</exception>
    public void MarkAlertSent(DateTimeOffset sentAt)
    {
        if (Alert == AlertKind.None)
        {
            throw new InvalidOperationException($"Reading {Id} is in range; it has no alert to send.");
        }

        AlertSentAt = sentAt.ToUniversalTime();
        AlertClaimedAt = null;
    }

    /// <summary>Gives the alert back so the next sweep (on any instance) can try it again.</summary>
    public void ReleaseAlertClaim() => AlertClaimedAt = null;

    private static string? CanonicalUnit(string? unit) =>
        string.Equals(unit, MgDl, StringComparison.OrdinalIgnoreCase) ? MgDl
        : string.Equals(unit, MmolL, StringComparison.OrdinalIgnoreCase) ? MmolL
        : null;

    private static decimal? ToMgDl(string raw, string unit)
    {
        // AllowDecimalPoint only: no sign, no exponent, no whitespace and no thousands separator,
        // so "1,2" is rejected instead of becoming 12.
        if (!decimal.TryParse(raw, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        var mgDl = Math.Round(unit == MmolL ? value * MgDlPerMmolL : value, 1, MidpointRounding.AwayFromZero);
        return mgDl <= MaxStorable ? mgDl : null;
    }

    private static AlertKind AlertFor(decimal mgDl) =>
        mgDl < LowBelow ? AlertKind.Low : mgDl > HighAbove ? AlertKind.High : AlertKind.None;
}
