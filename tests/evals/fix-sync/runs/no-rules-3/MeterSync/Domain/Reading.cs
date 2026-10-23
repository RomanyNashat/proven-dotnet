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

    public static Reading FromMeter(int patientId, int meterId, string vendorId, string raw, string unit, decimal value, DateTimeOffset takenAt) => new()
    {
        PatientId = patientId,
        MeterId = meterId,
        VendorId = vendorId,
        Raw = raw,
        Unit = unit,
        ValueMgDl = value,
        TakenAt = takenAt,
        Alert = value < LowBelow ? AlertKind.Low : value > HighAbove ? AlertKind.High : AlertKind.None,
    };

    public void MarkAlertSent(DateTimeOffset at) => AlertSentAt = at;
}
