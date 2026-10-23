using MeterSync.Api;
using Microsoft.EntityFrameworkCore;

namespace MeterSync.Infrastructure;

/// <summary>A singleton, so it takes a context per call from the factory: a DbContext isn't thread-safe.</summary>
public sealed class PatientDirectory(IDbContextFactory<SyncDbContext> contexts)
{
    public async Task<IReadOnlyList<MeterResponse>> MetersOfAsync(int patientId, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        return await db.Meters.AsNoTracking()
            .Where(m => m.PatientId == patientId)
            .OrderBy(m => m.Id)
            .Select(m => new MeterResponse(m.SerialNumber, m.LinkedAt))
            .ToListAsync(ct);
    }
}
