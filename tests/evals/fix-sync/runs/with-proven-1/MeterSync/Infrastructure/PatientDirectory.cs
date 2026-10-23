using MeterSync.Application;
using Microsoft.EntityFrameworkCore;

namespace MeterSync.Infrastructure;

/// <summary>Scoped: it holds a DbContext, which serves one request at a time.</summary>
public sealed class PatientDirectory(SyncDbContext db) : IPatientMeters
{
    public async Task<IReadOnlyList<MeterSummary>> MetersOfAsync(int patientId, CancellationToken ct) =>
        await db.Meters
            .Where(m => m.PatientId == patientId)
            .OrderBy(m => m.Id)
            .Select(m => new MeterSummary(m.SerialNumber, m.LinkedAt))
            .ToListAsync(ct);
}
