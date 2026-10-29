namespace LabResults.Domain;

/// <summary>An SMS saved with the result it announces, sent by the worker. A pod that stops leaves it for the next.</summary>
public sealed class PendingSms(string to, string text)
{
    public int Id { get; private set; }

    public string To { get; private set; } = to;

    public string Text { get; private set; } = text;

    public DateTimeOffset? SentAt { get; private set; }

    public void MarkSent(DateTimeOffset at) => SentAt = at;
}
