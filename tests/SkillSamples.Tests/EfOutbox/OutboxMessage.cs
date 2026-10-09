using System.Text.Json;

namespace SkillSamples.EfOutbox;

public sealed class OutboxMessage
{
    public long Id { get; private set; }                    // bigint identity: the order, and the id consumers dedupe on
    public required string Type { get; init; }
    public required string AggregateKey { get; init; }      // the Kafka key: one aggregate's events stay in order
    public required string Payload { get; init; }           // jsonb; ids, not whole documents
    public required DateTimeOffset OccurredAt { get; init; }
    public DateTimeOffset? PublishedAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }

    public static OutboxMessage From(IDomainEvent domainEvent, string aggregateKey, DateTimeOffset now) => new()
    {
        Type = domainEvent.GetType().Name,
        AggregateKey = aggregateKey,
        Payload = JsonSerializer.Serialize(domainEvent, domainEvent.GetType()),
        OccurredAt = now,
    };
}
