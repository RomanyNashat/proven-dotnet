using MeterSync.Application;
using Microsoft.EntityFrameworkCore;

namespace MeterSync.Infrastructure;

/// <summary>Reads about one patient. Scoped: it holds the request's DbContext.</summary>
public sealed class PatientDirectory(SyncDbContext db)
{
    public async Task<IReadOnlyList<LinkedMeter>> MetersOfAsync(int patientId, CancellationToken ct) =>
        await db.Meters
            .Where(m => m.PatientId == patientId)
            .OrderBy(m => m.Id)
            .Select(m => new LinkedMeter(m.SerialNumber, m.LinkedAt))
            .ToListAsync(ct);
}
