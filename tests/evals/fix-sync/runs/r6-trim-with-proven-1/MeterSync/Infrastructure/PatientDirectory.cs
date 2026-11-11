using MeterSync.Api;
using Microsoft.EntityFrameworkCore;

namespace MeterSync.Infrastructure;

public sealed class PatientDirectory(SyncDbContext db)
{
    public async Task<IReadOnlyList<MeterResponse>> MetersOfAsync(int patientId, CancellationToken ct) =>
        await db.Meters.AsNoTracking()
            .Where(m => m.PatientId == patientId)
            .OrderBy(m => m.Id)
            .Select(m => new MeterResponse(m.SerialNumber, m.LinkedAt))
            .ToListAsync(ct);
}
