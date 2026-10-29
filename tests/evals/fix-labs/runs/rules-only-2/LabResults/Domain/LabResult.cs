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

    /// <summary>The <c>sub</c> of the clinician who recorded it.</summary>
    public string RecordedBy { get; private set; } = "";

    /// <summary>Records a result received at <paramref name="receivedAt"/> by the clinician <paramref name="recordedBy"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="recordedBy"/> is empty.</exception>
    public static LabResult Record(
        int patientId, string test, decimal value, string unit, string? notes, string recordedBy, DateTimeOffset receivedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordedBy);

        // PostgreSQL keeps microseconds; truncating here makes the returned result match what a later read gives,
        // which the paging cursor depends on.
        var utc = receivedAt.ToUniversalTime();
        return new LabResult
        {
            PatientId = patientId,
            Test = test,
            Value = value,
            Unit = unit,
            Notes = notes,
            RecordedBy = recordedBy,
            ReceivedAt = utc.AddTicks(-(utc.UtcTicks % TimeSpan.TicksPerMicrosecond)),
        };
    }
}
