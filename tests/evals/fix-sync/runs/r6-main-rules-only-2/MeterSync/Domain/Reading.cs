namespace MeterSync.Domain;

public enum AlertKind
{
    None,
    Low,
    High,
}

/// <summary>Where a reading's SMS alert stands.</summary>
public enum AlertState
{
    /// <summary>In range: nothing to send.</summary>
    NotNeeded,

    /// <summary>Out of range and waiting to be texted.</summary>
    Pending,

    /// <summary>Texted.</summary>
    Sent,

    /// <summary>Out of range but too old to text (history imported when a meter is linked, late uploads).</summary>
    Stale,

    /// <summary>The SMS gateway refused the message (e.g. a bad number); retrying won't help.</summary>
    Rejected,
}

public sealed class Reading
{
    // The care team's thresholds, in mg/dL.
    public const decimal LowBelow = 70m;
    public const decimal HighAbove = 250m;

    // Only readings this fresh get an SMS. The vendor returns a meter's whole history, so without this
    // a newly linked meter would text the patient about every out-of-range reading it ever took.
    public static readonly TimeSpan AlertWindow = TimeSpan.FromHours(12);

    private Reading()
    {
    }

    public int Id { get; private set; }

    public int PatientId { get; private set; }

    public int MeterId { get; private set; }

    public string VendorId { get; private set; } = "";

    /// <summary>The value as the meter showed it.</summary>
    public string Raw { get; private set; } = "";

    public string Unit { get; private set; } = "";

    /// <summary>The value in mg/dL; null when the meter showed HI or LO (see <see cref="Raw"/>).</summary>
    public decimal? ValueMgDl { get; private set; }

    public DateTimeOffset TakenAt { get; private set; }

    public AlertKind Alert { get; private set; }

    public AlertState AlertState { get; private set; }

    /// <summary>While set and in the future, one pod owns sending this reading's alert.</summary>
    public DateTimeOffset? AlertClaimedUntil { get; private set; }

    public DateTimeOffset? AlertSentAt { get; private set; }

    /// <summary>Creates a reading from the meter vendor's data.</summary>
    /// <param name="now">The sync time, used to decide whether the reading is fresh enough to alert on.</param>
    public static Reading FromMeter(int patientId, int meterId, string vendorId, string raw, Glucose glucose, DateTimeOffset takenAt, DateTimeOffset now)
    {
        var alert = glucose.Classify(LowBelow, HighAbove);
        var fresh = now - takenAt <= AlertWindow;
        return new Reading
        {
            PatientId = patientId,
            MeterId = meterId,
            VendorId = vendorId,
            Raw = raw.Trim(),
            Unit = glucose.Unit,
            ValueMgDl = glucose.MgDl,
            TakenAt = takenAt.ToUniversalTime(),
            Alert = alert,
            AlertState = alert == AlertKind.None ? AlertState.NotNeeded : fresh ? AlertState.Pending : AlertState.Stale,
        };
    }
}
