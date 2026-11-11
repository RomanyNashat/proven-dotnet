using MeterSync.Domain;
using MeterSync.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MeterSync.Application;

/// <summary>Links a meter to the calling patient.</summary>
public sealed class MeterLinker(SyncDbContext db, TimeProvider time)
{
    /// <summary>Links <paramref name="serialNumber"/> to the caller, if <paramref name="nationalId"/> is the caller's own.</summary>
    /// <param name="callerPatientId">The patient id from the caller's token.</param>
    /// <param name="nationalId">The national id in the request; must be the caller's.</param>
    /// <param name="serialNumber">An already validated serial number.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>The outcome, and the meter when linked.</returns>
    public async Task<(LinkOutcome Outcome, Meter? Meter)> LinkAsync(
        int callerPatientId, string nationalId, string serialNumber, CancellationToken ct)
    {
        var ownsNationalId = await db.Patients.AnyAsync(p => p.Id == callerPatientId && p.NationalId == nationalId, ct);
        if (!ownsNationalId)
        {
            return (LinkOutcome.PatientNotFound, null);
        }

        if (await db.Meters.AnyAsync(m => m.SerialNumber == serialNumber, ct))
        {
            return (LinkOutcome.AlreadyLinked, null);
        }

        var meter = new Meter(callerPatientId, serialNumber, time.GetUtcNow());
        db.Meters.Add(meter);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            // Another request linked the same serial number between the check and the insert.
            return (LinkOutcome.AlreadyLinked, null);
        }

        return (LinkOutcome.Linked, meter);
    }
}
