namespace HomeReadings.Domain;

/// <summary>A high reading waiting to be sent to the patient's care team.</summary>
public sealed class CareTeamAlert
{
    public const int LastErrorMaxLength = 100;

    private CareTeamAlert()
    {
    }

    public CareTeamAlert(int readingId, DateTime createdAt)
    {
        ReadingId = readingId;
        CreatedAt = createdAt;
    }

    // Created with its reading, which fills in ReadingId when both are saved together.
    internal CareTeamAlert(DateTime createdAt)
    {
        CreatedAt = createdAt;
    }

    public int Id { get; private set; }
    public int ReadingId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? SentAt { get; private set; }

    /// <summary>Failed sends so far.</summary>
    public int Attempts { get; private set; }

    /// <summary>The last failure: the exception type and status only, never its message, which can echo patient data.</summary>
    public string? LastError { get; private set; }

    public DateTime? LastAttemptAt { get; private set; }

    public void MarkSent(DateTime at) => SentAt = at;

    public void RecordFailure(string error, DateTime at)
    {
        Attempts++;
        LastError = error.Length > LastErrorMaxLength ? error[..LastErrorMaxLength] : error;
        LastAttemptAt = at;
    }
}
