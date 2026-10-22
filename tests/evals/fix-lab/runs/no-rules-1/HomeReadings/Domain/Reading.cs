namespace HomeReadings.Domain;

/// <summary>A blood glucose reading a patient's meter sent through the app, in mmol/L or mg/dL.</summary>
public sealed class Reading
{
    /// <summary>At or above this the care team is told (mmol/L; mg/dL readings are converted first).</summary>
    public const decimal AlertLevel = 11.1m;

    public const string MmolPerL = "mmol/L";
    public const string MgPerDl = "mg/dL";

    /// <summary>mg/dL per mmol/L for glucose.</summary>
    public const decimal MgPerDlPerMmol = 18.016m;

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

    /// <summary>The value in mmol/L whatever unit the meter used.</summary>
    public decimal ValueInMmolPerL => ToMmolPerL(Value, Unit);

    public bool NeedsAlert => ValueInMmolPerL >= AlertLevel;

    public static decimal ToMmolPerL(decimal value, string unit) =>
        string.Equals(unit, MgPerDl, StringComparison.OrdinalIgnoreCase)
            ? Math.Round(value / MgPerDlPerMmol, 2)
            : value;

    public void AttachPhoto(string path) => PhotoPath = path;
}
