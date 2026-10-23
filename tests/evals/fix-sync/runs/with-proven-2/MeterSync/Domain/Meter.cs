namespace MeterSync.Domain;

public sealed class Meter(int patientId, string serialNumber, DateTimeOffset linkedAt)
{
    public const int SerialNumberMaxLength = 40;

    public int Id { get; private set; }

    public int PatientId { get; private set; } = patientId;

    /// <summary>The serial as the patient typed it. The vendor is called with it, and GET /meters returns it.</summary>
    public string SerialNumber { get; private set; } = serialNumber;

    /// <summary>
    /// The serial for uniqueness. PostgreSQL compares strings case-sensitively, so without this "ab12" and
    /// "AB12" would be two meters, linked to two patients.
    /// </summary>
    public string SerialKey { get; private set; } = KeyOf(serialNumber);

    public DateTimeOffset LinkedAt { get; private set; } = linkedAt;

    public static string KeyOf(string serialNumber) => serialNumber.Trim().ToUpperInvariant();
}
