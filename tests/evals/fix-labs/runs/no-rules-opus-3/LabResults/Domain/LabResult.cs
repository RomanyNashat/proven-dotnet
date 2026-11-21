namespace LabResults.Domain;

public sealed class LabResult
{
    public const int MaxTestLength = 40;
    public const int MaxUnitLength = 16;
    public const int MaxNotesLength = 2000;
    public const int MaxRecordedByLength = 64;

    /// <summary>The column is numeric(10,3): at most 7 integer digits and 3 decimals.</summary>
    public const decimal MaxAbsValue = 9_999_999.999m;
    public const int MaxValueScale = 3;

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
        ReceivedAt = receivedAt,
    };
}
