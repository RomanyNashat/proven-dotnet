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
        // Store exactly what PostgreSQL keeps (numeric(10,3), microsecond timestamptz in UTC), so the result
        // returned by POST is identical to the one later read back and the paging cursor round-trips.
        Value = decimal.Round(value, ValueScale, MidpointRounding.AwayFromZero),
        Unit = unit,
        Notes = notes,
        ReceivedAt = TruncateToMicroseconds(receivedAt.ToUniversalTime()),
    };

    public const int TestMaxLength = 40;
    public const int UnitMaxLength = 16;
    public const int NotesMaxLength = 2000;
    public const int ValueScale = 3;

    /// <summary>numeric(10,3) holds at most 7 integer digits.</summary>
    public const decimal ValueLimit = 10_000_000m;

    private static DateTimeOffset TruncateToMicroseconds(DateTimeOffset value) =>
        new(value.UtcTicks - (value.UtcTicks % (TimeSpan.TicksPerMillisecond / 1000)), TimeSpan.Zero);
}
