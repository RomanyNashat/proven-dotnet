namespace HomeReadings.Domain;

/// <summary>A blood glucose reading a patient's meter sent through the app, in the meter's unit.</summary>
public sealed class Reading
{
    /// <summary>At or above this the care team is told (mmol/L; 200 mg/dL).</summary>
    public const decimal AlertLevel = 11.1m;

    private Reading()
    {
    }

    public Reading(int patientId, string nationalId, MeterValue meter, string unit, DateTime collectedAt, string? notes, DateTime receivedAt)
    {
        PatientId = patientId;
        NationalId = nationalId;
        Value = meter.Number;
        OutOfRange = meter.Limit;
        Unit = unit;
        CollectedAt = collectedAt;
        Notes = notes;
        ReceivedAt = receivedAt;
    }

    public Reading(int patientId, string nationalId, decimal value, string unit, DateTime collectedAt, string? notes, DateTime receivedAt)
        : this(patientId, nationalId, new MeterValue(value, null), unit, collectedAt, notes, receivedAt)
    {
    }

    public int Id { get; private set; }
    public int PatientId { get; private set; }
    public string NationalId { get; private set; } = "";

    /// <summary>The number the meter showed; null when it showed HI or LO.</summary>
    public decimal? Value { get; private set; }

    /// <summary>HI or LO when the meter couldn't measure the reading.</summary>
    public MeterLimit? OutOfRange { get; private set; }

    public string Unit { get; private set; } = "";
    public DateTime CollectedAt { get; private set; }
    public string? Notes { get; private set; }

    /// <summary>The photo's key in the photo store (relative to its root), never a client file name.</summary>
    public string? PhotoPath { get; private set; }

    public DateTime ReceivedAt { get; private set; }

    /// <summary>The value in mmol/L, the unit the care team and the alert level use.</summary>
    public decimal? ValueInMmolPerLitre => Value is { } value ? GlucoseUnit.ToMmolPerLitre(value, Unit) : null;

    /// <summary>HI is above any meter's range, so it always alerts.</summary>
    public bool NeedsAlert => OutOfRange == MeterLimit.High || ValueInMmolPerLitre >= AlertLevel;

    public void AttachPhoto(string key) => PhotoPath = key;
}
