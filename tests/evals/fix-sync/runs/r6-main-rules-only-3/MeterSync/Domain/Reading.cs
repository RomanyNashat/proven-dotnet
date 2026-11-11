using System.Globalization;

namespace MeterSync.Domain;

public enum AlertKind
{
    None,
    Low,
    High,
}

public enum AlertStatus
{
    /// <summary>The reading is in range; nobody gets texted.</summary>
    NotNeeded,

    /// <summary>Out of range and not texted yet.</summary>
    Pending,

    /// <summary>The patient was texted.</summary>
    Sent,

    /// <summary>Out of range, but too old to text by the time we saw it (backfill after linking, or a long outage).</summary>
    Stale,

    /// <summary>Texting failed <see cref="Reading.MaxAlertAttempts"/> times; someone has to look at it.</summary>
    Failed,
}

public sealed class Reading
{
    // The care team's thresholds, in mg/dL.
    public const decimal LowBelow = 70m;
    public const decimal HighAbove = 250m;

    public const int MaxAlertAttempts = 5;

    public const string MgDl = "mg/dL";
    public const string MmolL = "mmol/L";
    public const decimal MgDlPerMmolL = 18m;

    // What the meter shows when the value is beyond its range (VENDOR.md).
    public const string AboveRange = "HI";
    public const string BelowRange = "LO";

    private Reading()
    {
    }

    public int Id { get; private set; }

    public int PatientId { get; private set; }

    public int MeterId { get; private set; }

    public string VendorId { get; private set; } = "";

    /// <summary>The value as the meter showed it (a number, <c>HI</c> or <c>LO</c>).</summary>
    public string Raw { get; private set; } = "";

    /// <summary>The unit the meter was set to, <see cref="MgDl"/> or <see cref="MmolL"/>.</summary>
    public string Unit { get; private set; } = "";

    /// <summary>The value in mg/dL, or <c>null</c> when the meter showed HI or LO.</summary>
    public decimal? ValueMgDl { get; private set; }

    public DateTimeOffset TakenAt { get; private set; }

    public AlertKind Alert { get; private set; }

    public AlertStatus AlertStatus { get; private set; }

    /// <summary>Set while one instance is texting the patient, so another instance doesn't text them too.</summary>
    public DateTimeOffset? AlertClaimedUntil { get; private set; }

    public int AlertAttempts { get; private set; }

    public DateTimeOffset? AlertSentAt { get; private set; }

    /// <summary>Builds a reading from what the vendor sent, or returns <c>null</c> when the value or unit can't be read.</summary>
    /// <param name="patientId">The patient the meter is linked to.</param>
    /// <param name="meterId">The meter.</param>
    /// <param name="vendorId">The vendor's reading id.</param>
    /// <param name="raw">The value as the meter showed it.</param>
    /// <param name="unit">The meter's unit.</param>
    /// <param name="takenAt">When the reading was taken.</param>
    /// <param name="worthTexting">Whether an out-of-range reading is still recent enough to text the patient about.</param>
    /// <returns>The reading, or <c>null</c>.</returns>
    public static Reading? TryFromMeter(
        int patientId, int meterId, string vendorId, string raw, string unit, DateTimeOffset takenAt, bool worthTexting)
    {
        var canonicalUnit = CanonicalUnit(unit);
        if (canonicalUnit is null || !TryReadMgDl(raw.Trim(), canonicalUnit, out var mgDl, out var alert))
        {
            return null;
        }

        return new Reading
        {
            PatientId = patientId,
            MeterId = meterId,
            VendorId = vendorId,
            Raw = raw.Trim(),
            Unit = canonicalUnit,
            ValueMgDl = mgDl,
            TakenAt = takenAt.ToUniversalTime(),
            Alert = alert,
            AlertStatus = alert == AlertKind.None ? AlertStatus.NotNeeded
                : worthTexting ? AlertStatus.Pending
                : AlertStatus.Stale,
        };
    }

    public static string? CanonicalUnit(string unit) => unit.Trim() switch
    {
        var u when u.Equals(MgDl, StringComparison.OrdinalIgnoreCase) => MgDl,
        var u when u.Equals(MmolL, StringComparison.OrdinalIgnoreCase) => MmolL,
        _ => null,
    };

    /// <summary>Takes the alert for this instance until <paramref name="until"/>.</summary>
    /// <param name="now">The current time.</param>
    /// <param name="until">When the claim lapses if the instance dies before texting.</param>
    /// <returns><c>false</c> when the alert is not pending or another instance holds it.</returns>
    public bool TryClaimAlert(DateTimeOffset now, DateTimeOffset until)
    {
        if (AlertStatus != AlertStatus.Pending || AlertClaimedUntil > now)
        {
            return false;
        }

        AlertClaimedUntil = until;
        AlertAttempts++;
        return true;
    }

    /// <summary>
    /// Records a failed send. The claim is kept until it lapses, so the retry waits for it (the gateway
    /// has no idempotency key, and a timeout doesn't tell us whether the SMS went out). After
    /// <see cref="MaxAlertAttempts"/> tries the alert is given up.
    /// </summary>
    /// <returns><c>true</c> when the alert was given up.</returns>
    public bool AlertFailed()
    {
        if (AlertAttempts < MaxAlertAttempts)
        {
            return false;
        }

        AlertStatus = AlertStatus.Failed;
        AlertClaimedUntil = null;
        return true;
    }

    public void MarkAlertSent(DateTimeOffset at)
    {
        AlertStatus = AlertStatus.Sent;
        AlertSentAt = at;
        AlertClaimedUntil = null;
    }

    private static bool TryReadMgDl(string raw, string unit, out decimal? mgDl, out AlertKind alert)
    {
        if (raw.Equals(AboveRange, StringComparison.OrdinalIgnoreCase))
        {
            (mgDl, alert) = (null, AlertKind.High);
            return true;
        }

        if (raw.Equals(BelowRange, StringComparison.OrdinalIgnoreCase))
        {
            (mgDl, alert) = (null, AlertKind.Low);
            return true;
        }

        // AllowDecimalPoint only: "1,2" must not read as 12, and a glucose value is never negative.
        if (!decimal.TryParse(raw, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
            || value <= 0m || value > 9999m)
        {
            (mgDl, alert) = (null, AlertKind.None);
            return false;
        }

        var converted = Math.Round(unit == MmolL ? value * MgDlPerMmolL : value, 1, MidpointRounding.AwayFromZero);
        if (converted > 9999m)
        {
            // Not a real glucose value, and it wouldn't fit numeric(6,1).
            (mgDl, alert) = (null, AlertKind.None);
            return false;
        }

        mgDl = converted;
        alert = converted < LowBelow ? AlertKind.Low : converted > HighAbove ? AlertKind.High : AlertKind.None;
        return true;
    }
}
