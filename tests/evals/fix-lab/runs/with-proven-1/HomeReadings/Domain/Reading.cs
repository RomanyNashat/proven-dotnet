namespace HomeReadings.Domain;

/// <summary>A blood glucose reading a patient's meter sent through the app, in the unit the meter showed.</summary>
public sealed class Reading
{
    /// <summary>At or above this the care team is told (mmol/L).</summary>
    public const decimal AlertLevel = 11.1m;

    /// <summary>The same level in mg/dL (11.1 mmol/L × 18).</summary>
    public const decimal AlertLevelMgPerDecilitre = 200m;

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

    /// <summary><c>HI</c> or <c>LO</c> when the meter showed that instead of a number.</summary>
    public string? OutOfRange { get; private set; }

    public DateTime CollectedAt { get; private set; }
    public string? Notes { get; private set; }

    /// <summary>The photo's key under the photo store's root (not a full path).</summary>
    public string? PhotoPath { get; private set; }

    public DateTime ReceivedAt { get; private set; }

    /// <summary>The care team alert for this reading, when it needed one.</summary>
    public CareTeamAlert? Alert { get; private set; }

    public bool NeedsAlert => Value >= (string.Equals(Unit, GlucoseUnit.MgPerDecilitre, StringComparison.OrdinalIgnoreCase)
        ? AlertLevelMgPerDecilitre
        : AlertLevel);

    /// <summary>
    /// A new reading, with its care team alert when it needs one. Both are saved by the same SaveChanges, so a
    /// high reading is never stored without its alert.
    /// </summary>
    public static Reading Record(int patientId, string nationalId, MeterValue value, string unit, DateTime collectedAt, string? notes, DateTime receivedAt)
    {
        var reading = new Reading(patientId, nationalId, value.Value, unit, collectedAt, notes, receivedAt)
        {
            OutOfRange = value.OutOfRange,
        };
        if (reading.NeedsAlert)
        {
            reading.Alert = new CareTeamAlert(receivedAt);
        }

        return reading;
    }

    public void AttachPhoto(string path) => PhotoPath = path;
}
