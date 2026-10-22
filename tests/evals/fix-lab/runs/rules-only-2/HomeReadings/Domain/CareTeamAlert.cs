namespace HomeReadings.Domain;

/// <summary>
/// A high reading waiting to be sent to the patient's care team.
/// The care team's system has no idempotency key, so every send is a new alert to a nurse. An alert is
/// claimed before it is sent and is only released for another try when the send is known to have failed.
/// A claim that never ends in <see cref="SentAt"/> means the outcome is unknown, and a person checks it.
/// </summary>
public sealed class CareTeamAlert
{
    private CareTeamAlert()
    {
    }

    public CareTeamAlert(Reading reading, DateTime createdAt)
    {
        Reading = reading;
        CreatedAt = createdAt;
    }

    public CareTeamAlert(int readingId, DateTime createdAt)
    {
        ReadingId = readingId;
        CreatedAt = createdAt;
    }

    public int Id { get; private set; }
    public int ReadingId { get; private set; }
    public Reading? Reading { get; private set; }
    public DateTime CreatedAt { get; private set; }

    /// <summary>When a notifier started sending it. Null means it is waiting.</summary>
    public DateTime? ClaimedAt { get; private set; }

    public DateTime? SentAt { get; private set; }

    /// <summary>Sends that failed before the care team accepted the alert.</summary>
    public int FailedAttempts { get; private set; }

    public void MarkSent(DateTime at) => SentAt = at;
}
