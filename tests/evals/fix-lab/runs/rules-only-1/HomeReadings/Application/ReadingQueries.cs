using HomeReadings.Api;
using HomeReadings.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HomeReadings.Application;

public sealed record ReadingView(int Id, decimal Value, string Unit, DateTime CollectedAt, string? Notes);

/// <summary>Every query is filtered to the signed-in patient: reading ids are sequential and guessable.</summary>
public sealed class ReadingQueries(ReadingsDbContext db, ICurrentPatient patient, TimeProvider time)
{
    public Task<ReadingView?> GetAsync(int id, CancellationToken ct) =>
        db.Readings
            .Where(r => r.Id == id && r.PatientId == patient.Id)
            .Select(r => new ReadingView(r.Id, r.Value, r.Unit, r.CollectedAt, r.Notes))
            .SingleOrDefaultAsync(ct);

    /// <summary>The patient's readings from today (the Riyadh day), for the chart on the home screen.</summary>
    public async Task<IReadOnlyList<ReadingView>> TodayAsync(CancellationToken ct)
    {
        var (start, end) = Riyadh.DayOf(time.GetUtcNow());
        return await db.Readings
            .Where(r => r.PatientId == patient.Id && r.CollectedAt >= start && r.CollectedAt < end)
            .OrderBy(r => r.CollectedAt)
            .Select(r => new ReadingView(r.Id, r.Value, r.Unit, r.CollectedAt, r.Notes))
            .ToListAsync(ct);
    }

    /// <summary>
    /// The newest reading, shown on every app start. Not cached: it is one row from the
    /// (PatientId, CollectedAt) index, and a cache would show a stale value right after a new reading.
    /// </summary>
    public Task<ReadingView?> LatestAsync(CancellationToken ct) =>
        db.Readings
            .Where(r => r.PatientId == patient.Id)
            .OrderByDescending(r => r.CollectedAt)
            .ThenByDescending(r => r.Id)
            .Select(r => new ReadingView(r.Id, r.Value, r.Unit, r.CollectedAt, r.Notes))
            .FirstOrDefaultAsync(ct);
}
