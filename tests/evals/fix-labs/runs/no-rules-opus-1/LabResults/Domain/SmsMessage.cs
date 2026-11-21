namespace LabResults.Domain;

/// <summary>
/// An SMS waiting to be sent (transactional outbox). Written in the same transaction as the result it is about,
/// so a crash or a deploy cannot lose it; deleted once the gateway accepts it, so phone numbers are not kept.
/// </summary>
public sealed class SmsMessage
{
    private SmsMessage()
    {
    }

    public long Id { get; private set; }

    public string To { get; private set; } = "";

    public string Text { get; private set; } = "";

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset NextAttemptAt { get; private set; }

    public int Attempts { get; private set; }

    public const int MaxAttempts = 8;

    public static SmsMessage Create(string to, string text, DateTimeOffset now) => new()
    {
        To = to,
        Text = text,
        CreatedAt = now,
        NextAttemptAt = now,
    };

    /// <summary>Records a failed attempt and schedules the next one with exponential backoff.</summary>
    /// <returns><c>false</c> when the message has used up its attempts.</returns>
    public bool Failed(DateTimeOffset now)
    {
        Attempts++;
        NextAttemptAt = now + TimeSpan.FromSeconds(Math.Min(15 * Math.Pow(2, Attempts - 1), 3600));
        return Attempts < MaxAttempts;
    }
}
