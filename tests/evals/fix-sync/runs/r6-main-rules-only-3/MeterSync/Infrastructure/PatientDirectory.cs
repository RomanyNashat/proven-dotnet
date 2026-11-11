using Microsoft.EntityFrameworkCore;

namespace MeterSync.Infrastructure;

public sealed record LinkedMeter(string SerialNumber, DateTimeOffset LinkedAt);

public sealed class PatientDirectory(SyncDbContext db)
{
    /// <summary>The meters linked to one patient, oldest first.</summary>
    /// <param name="patientId">The patient.</param>
    /// <param name="ct">Cancels the query.</param>
    /// <returns>The meters.</returns>
    public async Task<IReadOnlyList<LinkedMeter>> MetersOfAsync(int patientId, CancellationToken ct) =>
        await db.Meters
            .Where(m => m.PatientId == patientId)
            .OrderBy(m => m.Id)
            .Select(m => new LinkedMeter(m.SerialNumber, m.LinkedAt))
            .ToListAsync(ct);
}
