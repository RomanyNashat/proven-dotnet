namespace MeterSync.Domain;

public sealed class Meter(int patientId, string serialNumber, DateTimeOffset linkedAt)
{
    public int Id { get; private set; }

    public int PatientId { get; private set; } = patientId;

    public string SerialNumber { get; private set; } = serialNumber;

    public DateTimeOffset LinkedAt { get; private set; } = linkedAt;

    /// <summary>
    /// Whether a reading belongs to this link. The vendor returns every reading the meter ever uploaded,
    /// including a previous owner's; only readings from the link on are this patient's to be alerted about.
    /// </summary>
    public bool Covers(DateTimeOffset takenAt) => takenAt >= LinkedAt;
}
