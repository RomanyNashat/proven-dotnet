namespace HomeReadings.Domain;

/// <summary>A blood glucose reading a patient's meter sent through the app, kept in the meter's unit.</summary>
public sealed class Reading
{
    /// <summary>At or above this the care team is told (mmol/L).</summary>
    public const decimal AlertLevel = 11.1m;

    /// <summary>The same alert level for meters that show mg/dL (11.1 mmol/L is the usual 200 mg/dL).</summary>
    public const decimal AlertLevelMgPerDl = 200m;

    public const string MmolPerLitre = "mmol/L";
    public const string MgPerDecilitre = "mg/dL";

    /// <summary>mg/dL per mmol/L for glucose.</summary>
    private const decimal MgPerDlPerMmol = 18.0182m;

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

    // The threshold is in mmol/L: a mg/dL meter's 95 is a normal reading, not an alert.
    public bool NeedsAlert => IsMgPerDl(Unit) ? Value >= AlertLevelMgPerDl : Value >= AlertLevel;

    /// <summary>The value in mmol/L, the unit the care team works in.</summary>
    public decimal ValueInMmolPerLitre => IsMgPerDl(Unit) ? Math.Round(Value / MgPerDlPerMmol, 1) : Value;

    /// <summary>Returns the canonical spelling of a unit the service understands, or null.</summary>
    public static string? NormalizeUnit(string? unit) => unit?.Trim() switch
    {
        { } u when u.Equals(MmolPerLitre, StringComparison.OrdinalIgnoreCase) => MmolPerLitre,
        { } u when u.Equals(MgPerDecilitre, StringComparison.OrdinalIgnoreCase) => MgPerDecilitre,
        _ => null,
    };

    public void AttachPhoto(string path) => PhotoPath = path;

    private static bool IsMgPerDl(string unit) => unit.Equals(MgPerDecilitre, StringComparison.OrdinalIgnoreCase);
}
