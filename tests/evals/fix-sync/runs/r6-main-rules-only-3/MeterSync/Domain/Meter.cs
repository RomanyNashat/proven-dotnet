namespace MeterSync.Domain;

public sealed class Meter(int patientId, string serialNumber, DateTimeOffset linkedAt)
{
    public const int SerialNumberMaxLength = 40;

    public int Id { get; private set; }

    public int PatientId { get; private set; } = patientId;

    public string SerialNumber { get; private set; } = serialNumber;

    public DateTimeOffset LinkedAt { get; private set; } = linkedAt;
}
