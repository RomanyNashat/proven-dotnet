using Microsoft.Extensions.Hosting;
using MongoDB.Bson;
using MongoDB.Driver;

namespace SkillSamples.Mongo;

public interface INotificationHandler
{
    Task HandleAsync(Notification notification, CancellationToken ct);
}

public sealed class NewNotificationsWatcher(IMongoDatabase db, ResumeTokens tokens, INotificationHandler handler) : BackgroundService
{
    public const string Stream = "notifications.inserts";
    private readonly IMongoCollection<Notification> _notifications = db.GetCollection<Notification>("notifications");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = new ChangeStreamOptions { StartAfter = await tokens.GetAsync(Stream, stoppingToken) };
        var pipeline = new EmptyPipelineDefinition<ChangeStreamDocument<Notification>>()
            .Match(c => c.OperationType == ChangeStreamOperationType.Insert);   // an insert carries the full document

        using var cursor = await _notifications.WatchAsync(pipeline, options, stoppingToken);
        while (await cursor.MoveNextAsync(stoppingToken))
        {
            foreach (var change in cursor.Current)
            {
                // No catch here. A failure stops the service, and the restart resumes from the last saved
                // token, so the failed event comes round again. Catching, logging and carrying on would save
                // the next event's token and lose this one for good. Retry transient errors inside the
                // handler; send a document that can never succeed to a dead-letter collection by its _id.
                await handler.HandleAsync(change.FullDocument, stoppingToken);
                await tokens.SaveAsync(Stream, change.ResumeToken, stoppingToken);   // only after it's handled
            }
        }
    }
}

public sealed class ResumeTokens(IMongoDatabase db)
{
    private readonly IMongoCollection<BsonDocument> _tokens = db.GetCollection<BsonDocument>("resumeTokens");

    public async Task<BsonDocument?> GetAsync(string stream, CancellationToken ct)
    {
        var saved = await _tokens.Find(new BsonDocument("_id", stream)).FirstOrDefaultAsync(ct);
        return saved?["token"].AsBsonDocument;
    }

    public Task SaveAsync(string stream, BsonDocument token, CancellationToken ct) =>
        _tokens.ReplaceOneAsync(
            new BsonDocument("_id", stream),
            new BsonDocument { { "_id", stream }, { "token", token } },
            new ReplaceOptions { IsUpsert = true },
            ct);
}
