using System.Linq.Expressions;
using HomeReadings.Domain;
using HomeReadings.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HomeReadings.Application;

/// <summary>The signed-in patient's own readings. Every query filters by the patient.</summary>
public sealed class ReadingQueries(ReadingsDbContext db, ICurrentPatient patient, TimeProvider time)
{
    private static readonly Expression<Func<Reading, ReadingView>> ToView = r =>
        new ReadingView(r.Id, r.Value, r.Unit, r.CollectedAt, r.Notes,
            r.OutOfRange == MeterLimit.High ? "HI" : r.OutOfRange == MeterLimit.Low ? "LO" : null);

    public Task<ReadingView?> GetAsync(int id, CancellationToken ct) =>
        // Ids are sequential: without the patient filter anyone could read anyone's readings. Someone
        // else's reading is NotFound, like one that doesn't exist.
        Mine().Where(r => r.Id == id).Select(ToView).SingleOrDefaultAsync(ct);

    /// <summary>The patient's readings from today (the Riyadh day), for the chart on the home screen.</summary>
    public async Task<IReadOnlyList<ReadingView>> TodayAsync(CancellationToken ct)
    {
        var (start, end) = RiyadhTime.DayInUtc(time.GetUtcNow());
        return await Mine()
            .Where(r => r.CollectedAt >= start && r.CollectedAt < end)
            .OrderBy(r => r.CollectedAt).ThenBy(r => r.Id)
            .Select(ToView)
            .ToListAsync(ct);
    }

    /// <summary>
    /// The newest reading, shown on every app start. Not cached: it's one seek on (PatientId, CollectedAt),
    /// and a per-pod cache would hide a reading the patient just saved through another pod.
    /// </summary>
    public Task<ReadingView?> LatestAsync(CancellationToken ct) =>
        Mine()
            .OrderByDescending(r => r.CollectedAt).ThenByDescending(r => r.Id)
            .Select(ToView)
            .FirstOrDefaultAsync(ct);

    private IQueryable<Reading> Mine()
    {
        var patientId = patient.Id;
        return db.Readings.AsNoTracking().Where(r => r.PatientId == patientId);
    }
}
