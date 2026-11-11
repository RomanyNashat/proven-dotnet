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

    /// <summary>The value as the meter showed it.</summary>
    public string Raw { get; private set; } = "";

    public string Unit { get; private set; } = "";

    public decimal ValueMgDl { get; private set; }

    public DateTimeOffset TakenAt { get; private set; }

    public AlertKind Alert { get; private set; }

    public DateTimeOffset? AlertSentAt { get; private set; }

    /// <summary>While in the future, one instance owns sending this alert; the others skip it.</summary>
    public DateTimeOffset? AlertClaimedUntil { get; private set; }

    public int AlertAttempts { get; private set; }

    public static Reading FromMeter(int patientId, int meterId, string vendorId, string raw, string unit, decimal value, DateTimeOffset takenAt) => new()
    {
        PatientId = patientId,
        MeterId = meterId,
        VendorId = vendorId,
        Raw = raw,
        Unit = unit,
        ValueMgDl = value,
        TakenAt = takenAt,
        Alert = Classify(value),
    };

    /// <summary>Classifies a blood glucose value against the care team's thresholds.</summary>
    /// <param name="valueMgDl">The value in mg/dL.</param>
    /// <returns>Low below 70, High above 250, otherwise None.</returns>
    public static AlertKind Classify(decimal valueMgDl) => valueMgDl switch
    {
        < LowBelow => AlertKind.Low,
        > HighAbove => AlertKind.High,
        _ => AlertKind.None,
    };
}
