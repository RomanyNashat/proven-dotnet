using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Activity;

public sealed class ActivityService(IMongoDatabase db, IProducer<string, string> producer, IOptions<ActivityOptions> options, TimeProvider time)
{
    private static readonly TimeSpan Riyadh = TimeSpan.FromHours(3);
    private IMongoCollection<DailySteps> Days => db.GetCollection<DailySteps>("daily_steps");

    public Dictionary<string, string[]> Validate(SyncRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.Days.Count > options.Value.MaxDaysPerSync)
        {
            errors["days"] = [$"at most {options.Value.MaxDaysPerSync} days per sync"];
        }

        if (request.Days.Any(d => d.Steps is < 0 or > 100_000))
        {
            errors["steps"] = ["0 to 100,000 per day"];
        }

        return errors;
    }

    public async Task SyncAsync(int userId, SyncRequest request, CancellationToken ct)
    {
        foreach (var day in request.Days)
        {
            await Days.ReplaceOneAsync(d => d.UserId == userId && d.Day == day.Day,
                new DailySteps(userId, day.Day, day.Steps), new ReplaceOptions { IsUpsert = true }, ct);
        }

        await producer.ProduceAsync("steps-synced",
            new Message<string, string> { Key = userId.ToString(), Value = JsonSerializer.Serialize(new { userId, days = request.Days.Select(d => d.Day) }) }, ct);
    }

    public async Task<DayTotal> TodayAsync(int userId, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(time.GetUtcNow().ToOffset(Riyadh).DateTime);
        var doc = await Days.Find(d => d.UserId == userId && d.Day == today).FirstOrDefaultAsync(ct);
        return new DayTotal(today, doc?.Steps ?? 0);
    }

    public async Task<IReadOnlyList<DayTotal>> HistoryAsync(int userId, DateOnly from, DateOnly to, CancellationToken ct) =>
        (await Days.Find(d => d.UserId == userId && d.Day >= from && d.Day <= to).SortBy(d => d.Day).ToListAsync(ct))
            .Select(d => new DayTotal(d.Day, d.Steps)).ToList();
}
