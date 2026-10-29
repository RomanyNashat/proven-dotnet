namespace LabResults.Domain;

/// <summary>
/// An outbox row: the "result ready" SMS for one result, written in the same save as the result so it
/// survives a restart. The phone is cleared once the message is sent or given up on, so the table does
/// not keep patients' numbers.
/// </summary>
public sealed class PendingSms
{
    public const int MaxAttempts = 5;
    public const int PhoneMaxLength = 13;
    public const int LastErrorMaxLength = 100;

    private PendingSms()
    {
    }

    public long Id { get; private set; }

    public int ResultId { get; private set; }

    public LabResult? Result { get; private set; }

    /// <summary>E.164 Saudi mobile, <c>+9665XXXXXXXX</c>; null once sent or abandoned.</summary>
    public string? Phone { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? SentAt { get; private set; }

    public int Attempts { get; private set; }

    /// <summary>The exception type of the last failure; never its message, which can echo the number.</summary>
    public string? LastError { get; private set; }

    public bool IsAbandoned => SentAt is null && Attempts >= MaxAttempts;

    /// <summary>Queues the SMS for <paramref name="result"/>; its id is filled in when both are saved.</summary>
    /// <param name="result">The result the patient is told about.</param>
    /// <param name="phone">The normalised mobile number.</param>
    /// <param name="now">The time it was queued.</param>
    /// <returns>The pending message.</returns>
    public static PendingSms For(LabResult result, string phone, DateTimeOffset now) => new()
    {
        Result = result,
        Phone = phone,
        CreatedAt = now,
    };

    /// <summary>Marks the message delivered to the gateway.</summary>
    /// <param name="now">When the gateway accepted it.</param>
    public void MarkSent(DateTimeOffset now)
    {
        SentAt = now;
        Phone = null;
    }

    /// <summary>Counts a failed attempt; after <see cref="MaxAttempts"/> the message is abandoned.</summary>
    /// <param name="errorType">The exception type name.</param>
    public void MarkFailed(string errorType)
    {
        Attempts++;
        LastError = errorType.Length > LastErrorMaxLength ? errorType[..LastErrorMaxLength] : errorType;
        if (IsAbandoned)
        {
            Phone = null;
        }
    }
}
