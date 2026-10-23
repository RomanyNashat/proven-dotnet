using MeterSync.Application;
using MeterSync.Domain;
using Microsoft.EntityFrameworkCore;

namespace MeterSync.Infrastructure;

public sealed class EfAlertStore(SyncDbContext db) : IAlertStore
{
    public async Task<IReadOnlyList<int>> DueAsync(DateTimeOffset now, int max, CancellationToken ct) =>
        await db.Readings
            .Where(r => r.Status == AlertStatus.Pending && (r.AlertClaimedUntil == null || r.AlertClaimedUntil < now))
            .OrderBy(r => r.Id)
            .Take(max)
            .Select(r => r.Id)
            .ToListAsync(ct);

    // One UPDATE that re-checks the row: when two pods race, PostgreSQL makes the second wait for the first,
    // re-reads the claimed row, and updates nothing.
    public async Task<bool> TryClaimAsync(int readingId, DateTimeOffset now, DateTimeOffset until, CancellationToken ct) =>
        await db.Readings
            .Where(r => r.Id == readingId
                && r.Status == AlertStatus.Pending
                && (r.AlertClaimedUntil == null || r.AlertClaimedUntil < now))
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.AlertClaimedUntil, until), ct) == 1;

    public async Task<DueAlert?> LoadAsync(int readingId, CancellationToken ct)
    {
        var reading = await db.Readings.SingleOrDefaultAsync(r => r.Id == readingId, ct);
        if (reading is null)
        {
            return null;
        }

        var phone = await db.Patients.Where(p => p.Id == reading.PatientId).Select(p => p.Phone).SingleOrDefaultAsync(ct);
        return new DueAlert(reading, phone);
    }

    public Task SaveAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
