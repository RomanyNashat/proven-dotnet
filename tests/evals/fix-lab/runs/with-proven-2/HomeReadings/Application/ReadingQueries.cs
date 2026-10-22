using HomeReadings.Api;
using HomeReadings.Domain;
using HomeReadings.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace HomeReadings.Application;

public sealed record ReadingView(int Id, decimal Value, string Unit, DateTime CollectedAt, string? Notes);

public sealed class ReadingQueries(ReadingsDbContext db, ICurrentPatient patient, TimeProvider time, IMemoryCache cache)
{
    /// <summary>One of the patient's own readings. Ids are sequential, so someone else's is a 404, not a leak.</summary>
    public Task<ReadingView?> GetAsync(int id, CancellationToken ct)
    {
        var patientId = patient.Id;
        return db.Readings.AsNoTracking()
            .Where(r => r.Id == id && r.PatientId == patientId)
            .Select(r => new ReadingView(r.Id, r.Value, r.Unit, r.CollectedAt, r.Notes))
            .SingleOrDefaultAsync(ct);
    }

    /// <summary>The patient's readings from today in Riyadh, for the chart on the home screen.</summary>
    public async Task<IReadOnlyList<ReadingView>> TodayAsync(CancellationToken ct)
    {
        var patientId = patient.Id;
        var (start, end) = RiyadhTime.TodayUtcRange(time.GetUtcNow());
        return await db.Readings.AsNoTracking()
            .Where(r => r.PatientId == patientId && r.CollectedAt >= start && r.CollectedAt < end)
            .OrderBy(r => r.CollectedAt)
            .ThenBy(r => r.Id)
            .Select(r => new ReadingView(r.Id, r.Value, r.Unit, r.CollectedAt, r.Notes))
            .ToListAsync(ct);
    }

    /// <summary>The newest reading, shown on every app start: cached per patient for a minute, dropped when they add one.</summary>
    public async Task<ReadingView?> LatestAsync(CancellationToken ct)
    {
        var patientId = patient.Id;
        return await cache.GetOrCreateAsync(LatestReadingCache.Key(patientId), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = LatestReadingCache.Lifetime;
            return await db.Readings.AsNoTracking()
                .Where(r => r.PatientId == patientId)
                .OrderByDescending(r => r.CollectedAt)
                .ThenByDescending(r => r.Id)
                .Select(r => new ReadingView(r.Id, r.Value, r.Unit, r.CollectedAt, r.Notes))
                .FirstOrDefaultAsync(ct);
        });
    }
}
