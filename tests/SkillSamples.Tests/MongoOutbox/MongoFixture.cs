using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace SkillSamples.MongoOutbox;

/// <summary>A real MongoDB replica set (transactions need one). CI starts mongo:7 with --replSet rs0.</summary>
public sealed class MongoFixture : IAsyncLifetime
{
    static MongoFixture()
    {
        // The house conventions from the mongodb-patterns skill, so field names match the index scripts.
        SkillSamples.Mongo.MongoConventions.Register();
    }

    public static string Url { get; } =
        Environment.GetEnvironmentVariable("MONGO_URL") ?? "mongodb://localhost:27017/?replicaSet=rs0&directConnection=true";

    public IMongoClient Client { get; } = new MongoClient(Url);

    // Test classes run in parallel, so each fixture drops only the databases it created. Dropping every
    // "samples_" database here once dropped another class's database in the middle of its test.
    private readonly System.Collections.Concurrent.ConcurrentBag<string> _created = [];

    public IMongoDatabase NewDatabase()
    {
        var name = $"samples_{Guid.NewGuid():N}";
        _created.Add(name);
        return Client.GetDatabase(name);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var name in _created)
        {
            await Client.DropDatabaseAsync(name);
        }
    }

    /// <summary>The same indexes as the skill's mongosh script (db-scripts/mongo/outbox-indexes.js).</summary>
    public static async Task CreateOutboxIndexesAsync(IMongoDatabase db)
    {
        await db.CreateCollectionAsync("outbox");
        await db.CreateCollectionAsync("appointments");
        var outbox = db.GetCollection<BsonDocument>("outbox");
        await outbox.Indexes.CreateManyAsync([
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys.Ascending("occurredAt").Ascending("_id"),
                new CreateIndexOptions<BsonDocument>
                {
                    Name = "ix_pending_occurredAt",
                    PartialFilterExpression = new BsonDocument("status", "Pending"),
                }),
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys.Ascending("publishedAt"),
                new CreateIndexOptions { Name = "ix_ttl_published_7d", ExpireAfter = TimeSpan.FromDays(7) }),
        ]);
    }
}
