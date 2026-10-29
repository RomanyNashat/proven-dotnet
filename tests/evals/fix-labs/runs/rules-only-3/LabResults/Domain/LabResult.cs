namespace LabResults.Domain;

public sealed class LabResult
{
    public const int TestMaxLength = 40;
    public const int UnitMaxLength = 16;
    public const int NotesMaxLength = 1000;
    public const int RecordedByMaxLength = 64;

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

    /// <summary>The <c>sub</c> of the clinician who recorded it.</summary>
    public string RecordedBy { get; private set; } = "";

    /// <summary>Records a result received from the lab.</summary>
    /// <param name="patientId">The patient.</param>
    /// <param name="test">The test's name.</param>
    /// <param name="value">The measured value.</param>
    /// <param name="unit">The value's unit.</param>
    /// <param name="notes">Optional free text.</param>
    /// <param name="recordedBy">The recording clinician's <c>sub</c>.</param>
    /// <param name="receivedAt">When it arrived.</param>
    /// <returns>The new result.</returns>
    /// <exception cref="ArgumentException">No clinician is given.</exception>
    public static LabResult Record(
        int patientId, string test, decimal value, string unit, string? notes, string recordedBy, DateTimeOffset receivedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordedBy);

        var utc = receivedAt.ToUniversalTime();
        return new()
        {
            PatientId = patientId,
            Test = test,
            Value = value,
            Unit = unit,
            Notes = notes,
            RecordedBy = recordedBy,

            // timestamptz keeps microseconds; truncating here makes the value we return when recording
            // identical to the one read back later, which is also what the page cursor compares against.
            ReceivedAt = new DateTimeOffset(utc.UtcTicks - (utc.UtcTicks % 10), TimeSpan.Zero),
        };
    }
}
