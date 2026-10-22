namespace HomeReadings.Domain;

/// <summary>A blood glucose reading a patient's meter sent through the app, in mmol/L.</summary>
public sealed class Reading
{
    /// <summary>At or above this the care team is told (mmol/L).</summary>
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

    /// <summary>mg/dL per mmol/L for glucose.</summary>
    public const decimal MgDlPerMmolL = 18.0182m;

    public const string UnitMmolL = "mmol/L";
    public const string UnitMgDl = "mg/dL";

    /// <summary>The value in mmol/L whatever unit the meter used.</summary>
    public decimal ValueMmolL => Unit == UnitMgDl ? Math.Round(Value / MgDlPerMmolL, 2) : Value;

    public bool NeedsAlert => ValueMmolL >= AlertLevel;

    public void AttachPhoto(string path) => PhotoPath = path;
}
