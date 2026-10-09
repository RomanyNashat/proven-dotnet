using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace SkillSamples.MongoOutbox;

public sealed class LeaseDocument
{
    [BsonId] public required string Name { get; init; }
    public required string Owner { get; init; }
    public required DateTime ExpiresAt { get; init; }
}

/// <summary>
/// Lets one relay instance at a time publish, so events go out in order. The holder renews before the
/// lease expires; if it dies, another pod takes over once it has.
/// </summary>
public sealed class RelayLease(IMongoDatabase db, TimeProvider time, string owner, TimeSpan duration)
{
    private const string Name = "outbox-relay";
    private readonly IMongoCollection<LeaseDocument> _leases = db.GetCollection<LeaseDocument>("leases");

    public async Task<bool> TryAcquireAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var filter = Builders<LeaseDocument>.Filter.Eq(l => l.Name, Name)
                   & (Builders<LeaseDocument>.Filter.Lt(l => l.ExpiresAt, now)
                      | Builders<LeaseDocument>.Filter.Eq(l => l.Owner, owner));
        var update = Builders<LeaseDocument>.Update
            .Set(l => l.Owner, owner)
            .Set(l => l.ExpiresAt, now + duration);
        try
        {
            await _leases.FindOneAndUpdateAsync(filter, update,
                new FindOneAndUpdateOptions<LeaseDocument> { IsUpsert = true }, ct);
            return true;
        }
        catch (MongoCommandException ex) when (ex.Code == 11000)
        {
            return false;   // someone else holds a live lease: the upsert hit the existing _id
        }
    }
}
