using MeterSync.Api;
using Microsoft.EntityFrameworkCore;

namespace MeterSync.Infrastructure;

public sealed class PatientDirectory(SyncDbContext db)
{
    /// <summary>The meters linked to one patient.</summary>
    /// <param name="patientId">The patient, from the caller's token.</param>
    /// <param name="ct">Cancels the query.</param>
    /// <returns>The patient's meters, in the order they were linked.</returns>
    public async Task<IReadOnlyList<MeterResponse>> MetersOfAsync(int patientId, CancellationToken ct) =>
        await db.Meters.AsNoTracking()
            .Where(m => m.PatientId == patientId)
            .OrderBy(m => m.Id)
            .Select(m => new MeterResponse(m.SerialNumber, m.LinkedAt))
            .ToListAsync(ct);
}
