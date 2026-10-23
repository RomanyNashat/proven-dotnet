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

    private Reading()
    {
    }

    public int Id { get; private set; }

    public int PatientId { get; private set; }

    public int MeterId { get; private set; }

    public string VendorId { get; private set; } = "";

    /// <summary>The value as the meter showed it: a number, HI or LO.</summary>
    public string Raw { get; private set; } = "";

    public string Unit { get; private set; } = "";

    /// <summary>The value in mg/dL whatever the meter's unit; null when the meter showed HI or LO.</summary>
    public decimal? ValueMgDl { get; private set; }

    public DateTimeOffset TakenAt { get; private set; }

    public AlertKind Alert { get; private set; }

    public DateTimeOffset? AlertSentAt { get; private set; }

    /// <summary>While this is in the future, one pod owns sending this reading's alert.</summary>
    public DateTimeOffset? AlertClaimedUntil { get; private set; }

    /// <summary>A reading the vendor reported for a linked meter.</summary>
    /// <param name="patientId">The patient the meter is linked to.</param>
    /// <param name="meterId">The meter.</param>
    /// <param name="vendorId">The vendor's id for the reading (unique across the vendor).</param>
    /// <param name="raw">The value as the meter showed it.</param>
    /// <param name="unit">The meter's unit.</param>
    /// <param name="value">The parsed value.</param>
    /// <param name="takenAt">When the reading was taken.</param>
    /// <param name="linkedAt">When the meter was linked. A reading taken before it is kept but raises no
    /// alert: the vendor returns the meter's whole history, and linking a meter must not text the patient
    /// about every old reading.</param>
    /// <returns>The reading, with the alert it raises.</returns>
    public static Reading FromMeter(
        int patientId,
        int meterId,
        string vendorId,
        string raw,
        string unit,
        GlucoseValue value,
        DateTimeOffset takenAt,
        DateTimeOffset linkedAt)
    {
        ArgumentNullException.ThrowIfNull(value);
        var takenAtUtc = takenAt.ToUniversalTime();
        return new Reading
        {
            PatientId = patientId,
            MeterId = meterId,
            VendorId = vendorId,
            Raw = raw,
            Unit = unit,
            ValueMgDl = value.MgDl,
            TakenAt = takenAtUtc,
            Alert = takenAtUtc < linkedAt ? AlertKind.None : AlertFor(value),
        };
    }

    /// <summary>The alert a value raises against the care team's thresholds.</summary>
    /// <param name="value">The value.</param>
    /// <returns>Low, High or None.</returns>
    public static AlertKind AlertFor(GlucoseValue value) => value switch
    {
        { BelowRange: true } => AlertKind.Low,
        { AboveRange: true } => AlertKind.High,
        { MgDl: < LowBelow } => AlertKind.Low,
        { MgDl: > HighAbove } => AlertKind.High,
        _ => AlertKind.None,
    };
}
