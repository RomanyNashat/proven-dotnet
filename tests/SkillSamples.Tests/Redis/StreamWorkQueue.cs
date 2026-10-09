using StackExchange.Redis;

namespace SkillSamples.Redis;

// A Redis Stream read by a consumer group: each entry goes to one consumer, and stays pending until it's
// acknowledged.
public sealed class StreamWorkQueue(IDatabase db, RedisKey stream, string group)
{
    public async Task EnsureGroupAsync()
    {
        try
        {
            await db.StreamCreateConsumerGroupAsync(stream, group, StreamPosition.Beginning, createStream: true);
        }
        catch (RedisServerException ex) when (ex.Message.StartsWith("BUSYGROUP", StringComparison.Ordinal))
        {
            // the group already exists
        }
    }

    public Task<RedisValue> AddAsync(NameValueEntry[] fields) =>
        db.StreamAddAsync(stream, fields, maxLength: 100_000, useApproximateMaxLength: true);   // bounded

    public Task<StreamEntry[]> ReadNewAsync(string consumer, int count) =>
        db.StreamReadGroupAsync(stream, group, consumer, StreamPosition.NewMessages, count);

    public Task AckAsync(RedisValue id) => db.StreamAcknowledgeAsync(stream, group, id);

    // Entries a consumer read and never acknowledged (it crashed). The consumer name is the pod name,
    // which changes on every deploy, so a restarted pod never sees its old pending entries: any live
    // consumer claims them once they've been idle long enough. Needs Redis 6.2+.
    public async Task<StreamEntry[]> ClaimAbandonedAsync(string consumer, TimeSpan minIdle, int count) =>
        (await db.StreamAutoClaimAsync(stream, group, consumer, (long)minIdle.TotalMilliseconds, "0-0", count)).ClaimedEntries;
}
