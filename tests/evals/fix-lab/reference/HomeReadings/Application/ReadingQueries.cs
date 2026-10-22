using HomeReadings.Api;
using HomeReadings.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace HomeReadings.Application;

public sealed record ReadingView(int Id, decimal Value, string Unit, DateTime CollectedAt, string? Notes);

public sealed class ReadingQueries(ReadingsDbContext db, ICurrentPatient patient, TimeProvider time, IMemoryCache cache)
{
    public static string LatestKey(int patientId) => $"readings:latest:{patientId}";

    public Task<ReadingView?> GetAsync(int id, CancellationToken ct) =>
        db.Readings.AsNoTracking()
            .Where(r => r.Id == id && r.PatientId == patient.Id)
            .Select(r => new ReadingView(r.Id, r.Value, r.Unit, r.CollectedAt, r.Notes))
            .SingleOrDefaultAsync(ct);

    /// <summary>The patient's readings from today, for the chart on the home screen.</summary>
    public async Task<IReadOnlyList<ReadingView>> TodayAsync(CancellationToken ct)
    {
        var local = TimeZoneInfo.ConvertTime(time.GetUtcNow(), Riyadh.Zone);
        var start = new DateTimeOffset(local.Date, local.Offset).UtcDateTime;
        var end = start.AddDays(1);
        return await db.Readings.AsNoTracking()
            .Where(r => r.PatientId == patient.Id && r.CollectedAt >= start && r.CollectedAt < end)
            .OrderBy(r => r.CollectedAt)
            .Select(r => new ReadingView(r.Id, r.Value, r.Unit, r.CollectedAt, r.Notes))
            .ToListAsync(ct);
    }

    /// <summary>The newest reading, shown on every app start: cached for a minute.</summary>
    public async Task<ReadingView?> LatestAsync(CancellationToken ct) =>
        await cache.GetOrCreateAsync(LatestKey(patient.Id), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
            return await db.Readings.AsNoTracking()
                .Where(r => r.PatientId == patient.Id)
                .OrderByDescending(r => r.CollectedAt)
                .Select(r => new ReadingView(r.Id, r.Value, r.Unit, r.CollectedAt, r.Notes))
                .FirstOrDefaultAsync(ct);
        });
}
