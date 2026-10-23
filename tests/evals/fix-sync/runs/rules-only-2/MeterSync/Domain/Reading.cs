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

    /// <summary>
    /// The vendor returns every reading a meter ever uploaded. Readings from before the patient linked the
    /// meter are history, not news: texting about last month's readings helps nobody. A reading taken
    /// shortly before linking (the patient measured, then linked) still alerts.
    /// </summary>
    public static readonly TimeSpan AlertHistoryGrace = TimeSpan.FromHours(24);

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

    /// <summary>What the patient is texted about. None when in range, or for history from before linking.</summary>
    public AlertKind Alert { get; private set; }

    /// <summary>When a pod took the alert to send it. Stops two pods texting the same reading.</summary>
    public DateTimeOffset? AlertClaimedAt { get; private set; }

    public DateTimeOffset? AlertSentAt { get; private set; }

    public static AlertKind Classify(decimal valueMgDl) =>
        valueMgDl < LowBelow ? AlertKind.Low : valueMgDl > HighAbove ? AlertKind.High : AlertKind.None;

    public static Reading FromMeter(
        int patientId,
        int meterId,
        DateTimeOffset meterLinkedAt,
        string vendorId,
        string raw,
        string unit,
        decimal valueMgDl,
        DateTimeOffset takenAt) => new()
    {
        PatientId = patientId,
        MeterId = meterId,
        VendorId = vendorId,
        Raw = raw,
        Unit = unit,
        ValueMgDl = valueMgDl,
        TakenAt = takenAt.ToUniversalTime(),
        // Compare the gap, not linkedAt - grace: that subtraction overflows for an early linkedAt.
        Alert = meterLinkedAt - takenAt > AlertHistoryGrace ? AlertKind.None : Classify(valueMgDl),
    };
}
