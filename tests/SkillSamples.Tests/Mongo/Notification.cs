using MongoDB.Bson;

namespace SkillSamples.Mongo;

public sealed class Notification
{
    public ObjectId Id { get; init; } = ObjectId.GenerateNewId();
    public int SchemaVersion { get; init; } = 2;
    public required int UserId { get; init; }            // the SQL user's int key
    public required string Title { get; init; }          // the validator caps it at 200
    public bool IsRead { get; init; }
    public required DateTime CreatedAt { get; init; }    // a BSON date, UTC; read back as DateTimeKind.Utc
    public List<DeliveryAttempt> Attempts { get; init; } = [];   // bounded: at most 5, the validator checks
}

public sealed record DeliveryAttempt(DateTime At, string Outcome);
