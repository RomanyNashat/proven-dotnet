using MeterSync.Application;
using Microsoft.EntityFrameworkCore;

namespace MeterSync.Infrastructure;

public sealed class PatientDirectory(SyncDbContext db)
{
    /// <summary>The meters linked to a patient, oldest link first.</summary>
    /// <param name="patientId">The patient (the caller).</param>
    /// <param name="ct">Cancels the query.</param>
    /// <returns>The patient's meters.</returns>
    public async Task<IReadOnlyList<LinkedMeter>> MetersOfAsync(int patientId, CancellationToken ct) =>
        await db.Meters.AsNoTracking()
            .Where(m => m.PatientId == patientId)
            .OrderBy(m => m.Id)
            .Select(m => new LinkedMeter(m.SerialNumber, m.LinkedAt))
            .ToListAsync(ct);
}
