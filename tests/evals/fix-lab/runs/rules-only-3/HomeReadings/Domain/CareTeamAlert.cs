namespace HomeReadings.Domain;

/// <summary>A high reading waiting to be sent to the patient's care team.</summary>
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

    public int Id { get; private set; }
    public int ReadingId { get; private set; }
    public Reading? Reading { get; private set; }
    public DateTime CreatedAt { get; private set; }

    /// <summary>
    /// When a notifier took the alert to send it. Only one notifier (across all pods) can hold an alert
    /// at a time, because the care team has no idempotency key and a second POST is a second nurse call.
    /// </summary>
    public DateTime? ClaimedAt { get; private set; }

    public DateTime? SentAt { get; private set; }
}
