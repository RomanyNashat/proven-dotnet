using MeterSync.Application;
using MeterSync.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MeterSync.Infrastructure;

public sealed class EfMeterLinks(SyncDbContext db) : IMeterLinks
{
    public Task<string?> NationalIdOfAsync(int patientId, CancellationToken ct) =>
        db.Patients.Where(p => p.Id == patientId).Select(p => p.NationalId).SingleOrDefaultAsync(ct);

    public Task<bool> SerialTakenAsync(string serialNumber, CancellationToken ct) =>
        db.Meters.AnyAsync(m => m.SerialNumber == serialNumber, ct);

    public async Task<bool> TryAddAsync(Meter meter, CancellationToken ct)
    {
        db.Meters.Add(meter);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Two requests for the same serial passed the check together; the unique index decided.
            db.Entry(meter).State = EntityState.Detached;
            return false;
        }
    }
}
