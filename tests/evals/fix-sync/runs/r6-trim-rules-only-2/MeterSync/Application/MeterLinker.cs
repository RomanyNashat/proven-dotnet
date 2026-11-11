using MeterSync.Domain;
using MeterSync.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MeterSync.Application;

public enum LinkOutcome
{
    Linked,
    PatientNotFound,
    AlreadyLinked,
}

/// <summary>Links a meter to the calling patient.</summary>
public sealed class MeterLinker(SyncDbContext db, TimeProvider time)
{
    /// <summary>Links the meter, if the national id is the caller's own.</summary>
    /// <param name="callerId">The authenticated patient's id.</param>
    /// <param name="nationalId">The national id given in the request.</param>
    /// <param name="serialNumber">The meter's serial number.</param>
    /// <param name="ct">Cancels the operation.</param>
    /// <returns>The outcome, and the meter when it was linked.</returns>
    public async Task<(LinkOutcome Outcome, LinkedMeter? Meter)> LinkAsync(int callerId, string nationalId, string serialNumber, CancellationToken ct)
    {
        // Filtered by the caller: a patient can only link a meter to themselves. Someone else's national id
        // answers exactly like an unknown one, so the endpoint can't be used to probe who is a patient.
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
            return (LinkOutcome.AlreadyLinked, null);
        }

        var meter = new Meter(patientId.Value, serialNumber, time.GetUtcNow());
        db.Meters.Add(meter);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Two requests for the same serial number raced past the check above; the unique index decided.
            return (LinkOutcome.AlreadyLinked, null);
        }

        return (LinkOutcome.Linked, new LinkedMeter(meter.SerialNumber, meter.LinkedAt));
    }
}
