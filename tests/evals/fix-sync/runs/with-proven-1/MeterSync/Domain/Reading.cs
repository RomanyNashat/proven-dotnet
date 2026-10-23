using System.Diagnostics.CodeAnalysis;

namespace MeterSync.Domain;

public sealed class Reading
{
    // The care team's thresholds, in mg/dL.
    public const decimal LowBelow = 70m;
    public const decimal HighAbove = 250m;

    // Column sizes, kept here so the domain refuses what the database can't store.
    public const int VendorIdMaxLength = 64;
    public const int RawMaxLength = 16;
    public const int UnitMaxLength = 8;

    private Reading()
    {
    }

    public int Id { get; private set; }

    public int PatientId { get; private set; }

    public int MeterId { get; private set; }

    public string VendorId { get; private set; } = "";

    /// <summary>The value as the meter showed it ("112", "HI").</summary>
    public string Raw { get; private set; } = "";

    public string Unit { get; private set; } = "";

    public decimal ValueMgDl { get; private set; }

    public DateTimeOffset TakenAt { get; private set; }

    public AlertKind Alert { get; private set; }

    public AlertStatus Status { get; private set; }

    public int AlertAttempts { get; private set; }

    /// <summary>A pod that is sending this alert holds it until then; another pod may take it after.</summary>
    public DateTimeOffset? AlertClaimedUntil { get; private set; }

    public DateTimeOffset? AlertSentAt { get; private set; }

    /// <summary>
    /// A reading from the vendor, for the patient the meter is linked to. False, with the reason, when the
    /// vendor sent something this service can't read or store.
    /// </summary>
    public static bool TryFromVendor(Meter meter, string vendorId, string raw, string unit, DateTimeOffset takenAt,
        [NotNullWhen(true)] out Reading? reading, [NotNullWhen(false)] out string? rejection)
    {
        reading = null;
        if (string.IsNullOrWhiteSpace(vendorId) || vendorId.Length > VendorIdMaxLength)
        {
            rejection = "the vendor id is empty or too long";
            return false;
        }

        if (raw is null || raw.Length > RawMaxLength || !Glucose.TryParse(raw, unit, out var canonicalUnit, out var glucose))
        {
            rejection = "the value or unit isn't a glucose reading";
            return false;
        }

        var alert = glucose.MgDl < LowBelow ? AlertKind.Low : glucose.MgDl > HighAbove ? AlertKind.High : AlertKind.None;
        reading = new Reading
        {
            PatientId = meter.PatientId,
            MeterId = meter.Id,
            VendorId = vendorId,
            Raw = raw,
            Unit = canonicalUnit,
            ValueMgDl = glucose.MgDl,
            TakenAt = takenAt.ToUniversalTime(),
            Alert = alert,
            Status = alert == AlertKind.None ? AlertStatus.NotNeeded
                : meter.Covers(takenAt) ? AlertStatus.Pending
                : AlertStatus.BeforeLink,
        };
        rejection = null;
        return true;
    }

    public void MarkAlertSent(DateTimeOffset at)
    {
        Status = AlertStatus.Sent;
        AlertSentAt = at;
        AlertClaimedUntil = null;
    }

    /// <summary>
    /// Counts a failed send and releases the claim, so the next sweep tries again; after
    /// <paramref name="maxAttempts"/> the alert is given up. True when it was.
    /// </summary>
    public bool RecordAlertFailure(int maxAttempts)
    {
        AlertAttempts++;
        AlertClaimedUntil = null;
        if (AlertAttempts >= maxAttempts)
        {
            Status = AlertStatus.GaveUp;
        }

        return Status == AlertStatus.GaveUp;
    }
}
