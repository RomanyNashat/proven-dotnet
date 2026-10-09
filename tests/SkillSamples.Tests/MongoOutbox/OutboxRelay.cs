using MongoDB.Driver;

namespace SkillSamples.MongoOutbox;

public interface IOutboxPublisher
{
    /// <summary>Publish to the broker (Kafka: key = AggregateId, header event-id = Id).</summary>
    Task PublishAsync(OutboxMessage message, CancellationToken ct);
}

/// <summary>
/// Publishes pending messages oldest first, and stops at the first failure so nothing overtakes it.
/// Delivery is at-least-once: a crash between publishing and marking means the message goes out again,
/// so consumers dedupe on the message Id.
/// </summary>
public sealed class OutboxRelay(IMongoDatabase db, IOutboxPublisher publisher, TimeProvider time)
{
    private readonly IMongoCollection<OutboxMessage> _outbox = db.GetCollection<OutboxMessage>("outbox");

    public async Task<int> PublishPendingAsync(int batchSize, CancellationToken ct)
    {
        var pending = await _outbox
            .Find(m => m.Status == OutboxStatus.Pending)
            .SortBy(m => m.OccurredAt).ThenBy(m => m.Id)
            .Limit(batchSize)
            .ToListAsync(ct);

        var published = 0;
        foreach (var message in pending)
        {
            try
            {
                await publisher.PublishAsync(message, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await _outbox.UpdateOneAsync(m => m.Id == message.Id,
                    Builders<OutboxMessage>.Update.Inc(m => m.Attempts, 1).Set(m => m.LastError, ex.GetType().Name),
                    cancellationToken: ct);
                break;   // keep the order: retry this one first on the next run
            }

            await _outbox.UpdateOneAsync(m => m.Id == message.Id,
                Builders<OutboxMessage>.Update
                    .Set(m => m.Status, OutboxStatus.Published)
                    .Set(m => m.PublishedAt, time.GetUtcNow().UtcDateTime),
                cancellationToken: ct);
            published++;
        }

        return published;
    }
}
