using MongoDB.Bson;
using MongoDB.Driver;

namespace SkillSamples.Mongo;

public sealed class NotificationQueries(IMongoDatabase db)
{
    private readonly IMongoCollection<Notification> _notifications = db.GetCollection<Notification>("notifications");

    // Keyset paging on _id, newest first, served by ix_userId_id (Equality on userId, then Sort and Range on _id).
    // The next page starts after the last _id the caller saw: a notification that arrives between two pages
    // doesn't repeat an item, and page 50 costs what page 1 costs. Skip would read and drop every skipped one.
    public async Task<IReadOnlyList<Notification>> GetPageAsync(int userId, ObjectId? after, int size, CancellationToken ct)
    {
        var filter = Builders<Notification>.Filter.Eq(n => n.UserId, userId);
        if (after is { } last)
        {
            filter &= Builders<Notification>.Filter.Lt(n => n.Id, last);
        }

        return await _notifications.Find(filter)
            .SortByDescending(n => n.Id)
            .Limit(size)
            .ToListAsync(ct);
    }
}
