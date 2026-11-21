namespace LabResults.Domain;

public sealed class LabResult
{
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

    public static LabResult Record(int patientId, string test, decimal value, string unit, string? notes, DateTimeOffset receivedAt) => new()
    {
        PatientId = patientId,
        Test = test,
        Value = value,
        Unit = unit,
        Notes = notes,
        // PostgreSQL timestamptz keeps microseconds; truncate now so the value we return equals the one we store
        // (and the one the pagination cursor is built from).
        ReceivedAt = new DateTimeOffset(receivedAt.UtcTicks - receivedAt.UtcTicks % 10, TimeSpan.Zero),
    };
}
