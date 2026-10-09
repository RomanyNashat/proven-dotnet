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

    public IMongoDatabase NewDatabase() => Client.GetDatabase($"samples_{Guid.NewGuid():N}");

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var name in await (await Client.ListDatabaseNamesAsync()).ToListAsync())
        {
            if (name.StartsWith("samples_", StringComparison.Ordinal)) await Client.DropDatabaseAsync(name);
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
