namespace LabResults.Domain;

public sealed class LabResult
{
    private const long TicksPerMicrosecond = TimeSpan.TicksPerMillisecond / 1000;

    private LabResult()
    {
    }

    public int Id { get; private set; }

    public int PatientId { get; private set; }

    public string Test { get; private set; } = "";

    public decimal Value { get; private set; }

    public string Unit { get; private set; } = "";

    public string? Notes { get; private set; }

    public DateTimeOffset ReceivedAt { get; private set; }

    /// <summary>The clinician who recorded it; set by the audit interceptor.</summary>
    public string RecordedBy { get; private set; } = "";

    /// <summary>A new result. <paramref name="receivedAt"/> is kept in UTC at microsecond precision, what timestamptz stores,
    /// so the value returned on create is the value read back later (and used in page cursors).</summary>
    public static LabResult Record(int patientId, string test, decimal value, string unit, string? notes, DateTimeOffset receivedAt) => new()
    {
        PatientId = patientId,
        Test = test,
        Value = value,
        Unit = unit,
        Notes = notes,
        ReceivedAt = new DateTimeOffset(receivedAt.UtcTicks - (receivedAt.UtcTicks % TicksPerMicrosecond), TimeSpan.Zero),
    };
}
