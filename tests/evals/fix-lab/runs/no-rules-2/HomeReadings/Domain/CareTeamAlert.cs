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

    public void MarkSent(DateTime at) => SentAt = at;
}
