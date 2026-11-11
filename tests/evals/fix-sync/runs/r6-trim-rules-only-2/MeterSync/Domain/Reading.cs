namespace MeterSync.Domain;

public sealed class Reading
{
    // An alert about a reading older than this is no longer actionable. It also keeps the first sync of a
    // newly linked meter (the vendor returns the meter's whole history) from texting the patient about
    // every old reading.
    public static readonly TimeSpan AlertWindow = TimeSpan.FromHours(2);

    private Reading()
    {
    }

    public int Id { get; private set; }

    public int PatientId { get; private set; }

    public int MeterId { get; private set; }

    public string VendorId { get; private set; } = "";

    /// <summary>The value as the meter showed it (a number, <c>HI</c> or <c>LO</c>).</summary>
    public string Raw { get; private set; } = "";

    public string Unit { get; private set; } = "";

    /// <summary>The value in mg/dL; null when the meter showed HI or LO (out of its range).</summary>
    public decimal? ValueMgDl { get; private set; }

    public DateTimeOffset TakenAt { get; private set; }

    public AlertKind Alert { get; private set; }

    public AlertState AlertState { get; private set; }

    /// <summary>While set and in the future, one sweeper owns sending this alert.</summary>
    public DateTimeOffset? AlertClaimedUntil { get; private set; }

    public DateTimeOffset? AlertSentAt { get; private set; }

    /// <summary>Builds a reading from what the meter uploaded.</summary>
    /// <param name="patientId">The patient the meter is linked to.</param>
    /// <param name="meterId">The meter's id.</param>
    /// <param name="vendorId">The vendor's reading id.</param>
    /// <param name="value">The interpreted glucose value.</param>
    /// <param name="takenAt">When the reading was taken, UTC.</param>
    /// <param name="now">The current time, to decide whether an alert is still worth sending.</param>
    /// <returns>The reading.</returns>
    public static Reading FromMeter(int patientId, int meterId, string vendorId, GlucoseValue value, DateTimeOffset takenAt, DateTimeOffset now) => new()
    {
        PatientId = patientId,
        MeterId = meterId,
        VendorId = vendorId,
        Raw = value.Raw,
        Unit = value.Unit,
        ValueMgDl = value.MgDl,
        TakenAt = takenAt,
        Alert = value.Alert,
        AlertState = value.Alert switch
        {
            AlertKind.None => AlertState.NotNeeded,
            _ when takenAt < now - AlertWindow => AlertState.Expired,
            _ => AlertState.Pending,
        },
    };
}
