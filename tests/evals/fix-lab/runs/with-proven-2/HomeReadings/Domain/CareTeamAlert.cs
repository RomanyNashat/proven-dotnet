namespace HomeReadings.Domain;

/// <summary>A high reading waiting to be sent to the patient's care team.</summary>
public sealed class CareTeamAlert
{
    /// <summary>The longest error text kept; it holds an exception type, never a message.</summary>
    public const int LastErrorMaxLength = 100;

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

    /// <summary>Failed sends so far. The least-tried alerts go first, so one the care team keeps refusing can't hold the rest back.</summary>
    public int Attempts { get; private set; }

    /// <summary>What the last failed send threw: the exception type only, since a message can echo patient data.</summary>
    public string? LastError { get; private set; }

    /// <summary>While set and in the future, one notifier holds this alert and no other pod sends it.</summary>
    public DateTime? ClaimedUntil { get; private set; }

    public void MarkSent(DateTime at)
    {
        SentAt = at;
        ClaimedUntil = null;
    }

    /// <summary>Records a failed send and releases the claim, so the next run tries again.</summary>
    /// <param name="error">The exception type, or a short reason.</param>
    public void RecordFailure(string error)
    {
        Attempts++;
        LastError = error.Length > LastErrorMaxLength ? error[..LastErrorMaxLength] : error;
        ClaimedUntil = null;
    }
}
