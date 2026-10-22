using HomeReadings.Api;
using HomeReadings.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HomeReadings.Application;

public sealed record ReadingView(int Id, decimal Value, string Unit, DateTime CollectedAt, string? Notes);

/// <summary>Reads the current patient's readings. Every query is filtered by the patient: ids are guessable.</summary>
public sealed class ReadingQueries(ReadingsDbContext db, ICurrentPatient patient, TimeProvider time)
{
    public Task<ReadingView?> GetAsync(int id, CancellationToken ct)
    {
        var patientId = patient.Id;
        return db.Readings.AsNoTracking()
            .Where(r => r.Id == id && r.PatientId == patientId)
            .Select(r => new ReadingView(r.Id, r.Value, r.Unit, r.CollectedAt, r.Notes))
            .SingleOrDefaultAsync(ct);
    }

    /// <summary>The patient's readings from today (the Riyadh day), for the chart on the home screen.</summary>
    public async Task<IReadOnlyList<ReadingView>> TodayAsync(CancellationToken ct)
    {
        var patientId = patient.Id;
        var (start, end) = RiyadhTime.DayOf(time.GetUtcNow());
        return await db.Readings.AsNoTracking()
            .Where(r => r.PatientId == patientId && r.CollectedAt >= start && r.CollectedAt < end)
            .OrderBy(r => r.CollectedAt)
            .Select(r => new ReadingView(r.Id, r.Value, r.Unit, r.CollectedAt, r.Notes))
            .ToListAsync(ct);
    }

    /// <summary>
    /// The newest reading, shown on every app start. Not cached: it is one index seek on
    /// (PatientId, CollectedAt), and a cache would show a stale reading right after the patient adds one.
    /// </summary>
    public Task<ReadingView?> LatestAsync(CancellationToken ct)
    {
        var patientId = patient.Id;
        return db.Readings.AsNoTracking()
            .Where(r => r.PatientId == patientId)
            .OrderByDescending(r => r.CollectedAt)
            .ThenByDescending(r => r.Id)
            .Select(r => new ReadingView(r.Id, r.Value, r.Unit, r.CollectedAt, r.Notes))
            .FirstOrDefaultAsync(ct);
    }
}
