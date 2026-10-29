namespace LabResults.Domain;

/// <summary>
/// An SMS waiting to be sent, written in the same SaveChanges as the result it announces, so a restart
/// can't lose it and a full in-memory queue can't block the request. The row is deleted once sent: the
/// phone number stays in the database only as long as the message is pending.
/// </summary>
public sealed class SmsOutboxMessage
{
    /// <summary>After this many failed sends the row is left for an operator, not retried.</summary>
    public const int MaxAttempts = 5;

    private SmsOutboxMessage()
    {
    }

    public long Id { get; private set; }

    public string Phone { get; private set; } = "";

    public DateTimeOffset CreatedAt { get; private set; }

    public int Attempts { get; private set; }

    /// <summary>The exception type of the last failure, never its message (it can echo the number).</summary>
    public string? LastError { get; private set; }

    /// <summary>A "your result is ready" message for <paramref name="phone"/>.</summary>
    /// <param name="phone">The validated recipient.</param>
    /// <param name="now">From the injected <see cref="TimeProvider"/>.</param>
    /// <returns>The new outbox row.</returns>
    public static SmsOutboxMessage ResultReady(string phone, DateTimeOffset now) => new() { Phone = phone, CreatedAt = now };

    /// <summary>Counts a failed send.</summary>
    /// <param name="errorType">The exception type name.</param>
    public void RecordFailure(string errorType)
    {
        Attempts++;
        LastError = errorType.Length > 100 ? errorType[..100] : errorType;
    }
}
