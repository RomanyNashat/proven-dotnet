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

    /// <summary>The value as the meter showed it: a number in <see cref="Unit"/>, or <c>HI</c> / <c>LO</c>.</summary>
    public string Raw { get; private set; } = "";

    public string Unit { get; private set; } = "";

    /// <summary>The value in mg/dL, whatever unit the meter showed. <c>HI</c> and <c>LO</c> store the meter's range limit.</summary>
    public decimal ValueMgDl { get; private set; }

    public DateTimeOffset TakenAt { get; private set; }

    public AlertKind Alert { get; private set; }

    public DateTimeOffset? AlertSentAt { get; private set; }

    /// <summary>While set and in the future, one pod is sending this reading's alert and the others leave it alone.</summary>
    public DateTimeOffset? AlertClaimedUntil { get; private set; }

    /// <summary>Creates a reading from the meter.</summary>
    /// <param name="patientId">The patient the meter is linked to.</param>
    /// <param name="meterId">The meter.</param>
    /// <param name="vendorId">The vendor's reading id.</param>
    /// <param name="raw">The value as the meter showed it.</param>
    /// <param name="unit">The unit the meter showed.</param>
    /// <param name="valueMgDl">The value converted to mg/dL.</param>
    /// <param name="takenAt">When it was taken, in UTC.</param>
    /// <returns>The reading, with its alert decided.</returns>
    public static Reading FromMeter(int patientId, int meterId, string vendorId, string raw, string unit, decimal valueMgDl, DateTimeOffset takenAt) => new()
    {
        PatientId = patientId,
        MeterId = meterId,
        VendorId = vendorId,
        Raw = raw,
        Unit = unit,
        ValueMgDl = valueMgDl,
        TakenAt = takenAt,
        Alert = Classify(valueMgDl),
    };

    /// <summary>Applies the care team's thresholds.</summary>
    /// <param name="valueMgDl">A blood glucose value in mg/dL.</param>
    /// <returns>The alert the value calls for.</returns>
    public static AlertKind Classify(decimal valueMgDl) =>
        valueMgDl < LowBelow ? AlertKind.Low : valueMgDl > HighAbove ? AlertKind.High : AlertKind.None;
}
