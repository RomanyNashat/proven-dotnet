namespace LabResults.Domain;

public sealed class LabResult
{
    public const int TestMaxLength = 40;
    public const int UnitMaxLength = 16;
    public const int NotesMaxLength = 1000;
    public const int RecordedByMaxLength = 64;

    // timestamptz stores microseconds: 10 ticks.
    private const long TicksPerMicrosecond = 10;

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

    /// <summary>Records a result for a patient.</summary>
    /// <param name="patientId">The patient.</param>
    /// <param name="test">The test name.</param>
    /// <param name="value">The measured value.</param>
    /// <param name="unit">The unit of <paramref name="value"/>.</param>
    /// <param name="notes">Optional free text.</param>
    /// <param name="receivedAt">When it was received; kept in UTC to the microsecond, as the column stores it.</param>
    /// <param name="recordedBy">The clinician's <c>sub</c>; never a placeholder.</param>
    /// <returns>The new result.</returns>
    /// <exception cref="ArgumentException"><paramref name="recordedBy"/> is blank or longer than the column.</exception>
    public static LabResult Record(
        int patientId, string test, decimal value, string unit, string? notes, DateTimeOffset receivedAt, string recordedBy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordedBy);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(recordedBy.Length, RecordedByMaxLength, nameof(recordedBy));

        var utc = receivedAt.ToUniversalTime();
        return new LabResult
        {
            PatientId = patientId,
            Test = test,
            Value = value,
            Unit = unit,
            Notes = notes,
            ReceivedAt = utc.AddTicks(-(utc.Ticks % TicksPerMicrosecond)),
            RecordedBy = recordedBy,
        };
    }
}
