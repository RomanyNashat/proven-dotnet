using System.Text.Json;
using MongoDB.Bson;

namespace SkillSamples.MongoOutbox;

public enum OutboxStatus { Pending, Published }

/// <summary>One event waiting to be published. Saved in the same transaction as the change it describes.</summary>
public sealed class OutboxMessage
{
    public ObjectId Id { get; init; } = ObjectId.GenerateNewId();   // also the event id consumers dedupe on
    public required string Type { get; init; }
    public required string AggregateId { get; init; }               // the Kafka key: one aggregate's events stay in order
    public required string Payload { get; init; }                   // JSON; keep it small, ids not documents
    public required DateTime OccurredAt { get; init; }
    public OutboxStatus Status { get; set; } = OutboxStatus.Pending;
    public DateTime? PublishedAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }

    public static OutboxMessage For<TEvent>(TEvent @event, string aggregateId, DateTime occurredAt)
        where TEvent : notnull => new()
        {
            Type = typeof(TEvent).Name,
            AggregateId = aggregateId,
            Payload = JsonSerializer.Serialize(@event),
            OccurredAt = occurredAt,
        };
}
