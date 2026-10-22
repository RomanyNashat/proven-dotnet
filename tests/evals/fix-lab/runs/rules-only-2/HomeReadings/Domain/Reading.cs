namespace HomeReadings.Domain;

/// <summary>A blood glucose reading a patient's meter sent through the app, in the unit the meter showed.</summary>
public sealed class Reading
{
    /// <summary>At or above this the care team is told (mmol/L; 200 mg/dL).</summary>
    public const decimal AlertLevel = 11.1m;

    private Reading()
    {
    }

    public Reading(int patientId, string nationalId, decimal value, string unit, DateTime collectedAt, string? notes, DateTime receivedAt)
    {
        if (!GlucoseUnits.TryNormalize(unit, out var canonicalUnit))
        {
            throw new ArgumentOutOfRangeException(nameof(unit), unit, "Unknown glucose unit.");
        }

        if (collectedAt.Kind != DateTimeKind.Utc || receivedAt.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Times are stored in UTC.", nameof(collectedAt));
        }

        PatientId = patientId;
        NationalId = nationalId;
        Value = value;
        Unit = canonicalUnit;
        CollectedAt = collectedAt;
        Notes = notes;
        ReceivedAt = receivedAt;
    }

    public int Id { get; private set; }
    public int PatientId { get; private set; }
    public string NationalId { get; private set; } = "";
    public decimal Value { get; private set; }
    public string Unit { get; private set; } = "";
    public DateTime CollectedAt { get; private set; }
    public string? Notes { get; private set; }
    public string? PhotoPath { get; private set; }
    public DateTime ReceivedAt { get; private set; }

    public decimal ValueInMmolPerL => GlucoseUnits.ToMmolPerL(Value, Unit);

    public bool NeedsAlert => ValueInMmolPerL >= AlertLevel;

    public void AttachPhoto(string path) => PhotoPath = path;
}
