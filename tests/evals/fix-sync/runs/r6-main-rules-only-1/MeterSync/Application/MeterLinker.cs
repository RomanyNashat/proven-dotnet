using MeterSync.Domain;
using MeterSync.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MeterSync.Application;

public enum LinkOutcome
{
    Linked,
    PatientNotFound,
    SerialTaken,
}

/// <summary>Links a meter to the calling patient.</summary>
public sealed class MeterLinker(SyncDbContext db, TimeProvider time)
{
    /// <summary>Links the meter, if the national id is the caller's own.</summary>
    /// <param name="callerId">The authenticated patient.</param>
    /// <param name="nationalId">The national id from the request.</param>
    /// <param name="serialNumber">The meter's serial number.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>The outcome, and the meter when linked.</returns>
    public async Task<(LinkOutcome Outcome, Meter? Meter)> LinkAsync(int callerId, string nationalId, string serialNumber, CancellationToken ct)
    {
        // The national id must belong to the caller. Otherwise anyone could attach a meter to another
        // patient's record, or probe which national ids are registered.
        var patientId = await db.Patients
            .Where(p => p.NationalId == nationalId && p.Id == callerId)
            .Select(p => (int?)p.Id)
            .SingleOrDefaultAsync(ct);
        if (patientId is null)
        {
            return (LinkOutcome.PatientNotFound, null);
        }

        if (await db.Meters.AnyAsync(m => m.SerialNumber == serialNumber, ct))
        {
            return (LinkOutcome.SerialTaken, null);
        }

        var meter = new Meter(patientId.Value, serialNumber, time.GetUtcNow());
        db.Meters.Add(meter);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            // Two requests (or pods) linked the same serial at once; the unique index decided.
            db.ChangeTracker.Clear();
            return (LinkOutcome.SerialTaken, null);
        }

        return (LinkOutcome.Linked, meter);
    }
}
