namespace HomeReadings.Domain;

/// <summary>
/// A high reading waiting to be sent to the patient's care team. Their system has no idempotency key:
/// every POST pages a nurse. So an alert is retried only when it certainly didn't arrive; when that can't
/// be known, it is set aside for a person to check (<see cref="NeedsFollowUpAt"/>) rather than sent again.
/// </summary>
public sealed class CareTeamAlert
{
    public const int LastErrorMaxLength = 100;

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
    public DateTime? SentAt { get; private set; }

    /// <summary>Set, and committed, just before the POST; cleared when the outcome is known.</summary>
    public DateTime? SendingStartedAt { get; private set; }

    /// <summary>The notifier stopped trying: delivery is unknown or was refused. A person checks with the care team.</summary>
    public DateTime? NeedsFollowUpAt { get; private set; }

    public int Attempts { get; private set; }

    /// <summary>The last failure: an HTTP status or an exception type, never a message (it can echo patient data).</summary>
    public string? LastError { get; private set; }

    public void StartSending(DateTime at)
    {
        SendingStartedAt = at;
        Attempts++;
    }

    public void MarkSent(DateTime at)
    {
        SentAt = at;
        SendingStartedAt = null;
    }

    /// <summary>It certainly didn't arrive: the next run sends it again.</summary>
    public void MarkNotDelivered(string reason)
    {
        SendingStartedAt = null;
        LastError = Bounded(reason);
    }

    /// <summary>It may have arrived, or the care team refused it: stop sending, a person follows up.</summary>
    public void MarkNeedsFollowUp(DateTime at, string reason)
    {
        NeedsFollowUpAt = at;
        SendingStartedAt = null;
        LastError = Bounded(reason);
    }

    private static string Bounded(string reason) => reason.Length <= LastErrorMaxLength ? reason : reason[..LastErrorMaxLength];
}
