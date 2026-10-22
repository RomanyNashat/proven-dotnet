namespace HomeReadings.Domain;

/// <summary>A blood glucose reading a patient's meter sent through the app, in the meter's unit (mmol/L or mg/dL).</summary>
public sealed class Reading
{
    /// <summary>At or above this the care team is told (mmol/L).</summary>
    public const decimal AlertLevel = 11.1m;

    /// <summary>The same level for a meter that reports in mg/dL.</summary>
    public const decimal AlertLevelMgPerDecilitre = 200m;

    /// <summary>The longest note kept; the column and the request validator use the same limit.</summary>
    public const int NotesMaxLength = 500;

    private Reading()
    {
    }

    public Reading(int patientId, string nationalId, decimal value, string unit, DateTime collectedAt, string? notes, DateTime receivedAt)
    {
        PatientId = patientId;
        NationalId = nationalId;
        Value = value;
        Unit = unit;
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

    public bool NeedsAlert => GlucoseUnits.IsMgPerDecilitre(Unit) ? Value >= AlertLevelMgPerDecilitre : Value >= AlertLevel;

    public void AttachPhoto(string path) => PhotoPath = path;
}
