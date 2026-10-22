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

    /// <summary>The value in mmol/L, whatever unit the meter showed.</summary>
    public decimal ValueInMmolPerLitre => GlucoseUnit.ToMmolPerLitre(Value, Unit);

    // Compared in mmol/L: 180 mg/dL is not "above 11.1", and 200 mg/dL is.
    public bool NeedsAlert => Math.Round(ValueInMmolPerLitre, 1, MidpointRounding.AwayFromZero) >= AlertLevel;

    public void AttachPhoto(string path) => PhotoPath = path;
}
