namespace HomeReadings.Domain;

/// <summary>A high reading waiting to be sent to the patient's care team.</summary>
public sealed class CareTeamAlert
{
    private CareTeamAlert()
    {
    }

    public CareTeamAlert(int readingId, DateTime createdAt)
    {
        ReadingId = readingId;
        CreatedAt = createdAt;
    }

    public int Id { get; private set; }
    public int ReadingId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? SentAt { get; private set; }

    /// <summary>
    /// While set and in the future, one notifier instance owns this alert. The care team's system has no
    /// idempotency key, so two pods sending the same alert would page a nurse twice.
    /// </summary>
    public DateTime? ClaimedUntil { get; private set; }

    public int Attempts { get; private set; }

    public void MarkSent(DateTime at)
    {
        SentAt = at;
        ClaimedUntil = null;
        Attempts++;
    }

    /// <summary>The send failed: let the next run pick it up again.</summary>
    public void Release()
    {
        ClaimedUntil = null;
        Attempts++;
    }
}
