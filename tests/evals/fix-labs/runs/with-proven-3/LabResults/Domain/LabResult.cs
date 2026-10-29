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

    /// <summary>A new result. <see cref="RecordedBy"/> is stamped at save time.</summary>
    /// <param name="patientId">The patient.</param>
    /// <param name="test">The test's name.</param>
    /// <param name="value">The measured value.</param>
    /// <param name="unit">The value's unit.</param>
    /// <param name="notes">Free text, up to 1000 characters.</param>
    /// <param name="receivedAt">When the result arrived.</param>
    /// <returns>The result.</returns>
    public static LabResult Record(int patientId, string test, decimal value, string unit, string? notes, DateTimeOffset receivedAt) => new()
    {
        PatientId = patientId,
        Test = test,
        Value = value,
        Unit = unit,
        Notes = notes,
        ReceivedAt = ToStoredPrecision(receivedAt),
    };

    // timestamptz keeps microseconds in UTC (Npgsql refuses a non-zero offset). Rounding here makes the
    // POST response show the value a later GET reads back, and gives the paging cursor an exact match.
    private static DateTimeOffset ToStoredPrecision(DateTimeOffset at)
    {
        var utc = at.ToUniversalTime();
        return utc.AddTicks(-(utc.Ticks % TimeSpan.TicksPerMicrosecond));
    }
}
