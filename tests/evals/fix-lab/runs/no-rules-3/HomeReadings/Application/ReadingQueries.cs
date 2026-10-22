using HomeReadings.Api;
using HomeReadings.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HomeReadings.Application;

public sealed record ReadingView(int Id, decimal Value, string Unit, DateTime CollectedAt, string? Notes);

public sealed class ReadingQueries(ReadingsDbContext db, ICurrentPatient patient, TimeProvider time)
{
    /// <summary>Patients are in Riyadh: UTC+3 all year, no daylight saving.</summary>
    private static readonly TimeSpan Riyadh = TimeSpan.FromHours(3);

    public Task<ReadingView?> GetAsync(int id, CancellationToken ct) =>
        db.Readings.AsNoTracking()
            .Where(r => r.Id == id && r.PatientId == patient.Id)
            .Select(r => new ReadingView(r.Id, r.Value, r.Unit, r.CollectedAt, r.Notes))
            .SingleOrDefaultAsync(ct);

    /// <summary>The patient's readings from today (Riyadh's day), for the chart on the home screen.</summary>
    public async Task<IReadOnlyList<ReadingView>> TodayAsync(CancellationToken ct)
    {
        var localNow = time.GetUtcNow().ToOffset(Riyadh);
        var start = new DateTimeOffset(localNow.Date, Riyadh).UtcDateTime;
        var end = start.AddDays(1);
        return await db.Readings.AsNoTracking()
            .Where(r => r.PatientId == patient.Id && r.CollectedAt >= start && r.CollectedAt < end)
            .OrderBy(r => r.CollectedAt)
            .ThenBy(r => r.Id)
            .Select(r => new ReadingView(r.Id, r.Value, r.Unit, r.CollectedAt, r.Notes))
            .ToListAsync(ct);
    }

    /// <summary>The patient's newest reading. Not cached: the shared cache key showed one patient's reading to the next.</summary>
    public Task<ReadingView?> LatestAsync(CancellationToken ct) =>
        db.Readings.AsNoTracking()
            .Where(r => r.PatientId == patient.Id)
            .OrderByDescending(r => r.CollectedAt)
            .ThenByDescending(r => r.Id)
            .Select(r => new ReadingView(r.Id, r.Value, r.Unit, r.CollectedAt, r.Notes))
            .FirstOrDefaultAsync(ct);
}
