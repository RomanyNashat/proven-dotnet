using ClinicQueue.Domain;
using ClinicQueue.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.Application;

/// <summary>Books a patient into a clinic's queue for a day.</summary>
public sealed class VisitBookings(QueueDbContext db, QueueSnapshotCache snapshots)
{
    /// <summary>The new visit, or null when the clinic doesn't exist.</summary>
    public async Task<Visit?> BookAsync(int clinicId, int patientId, DateOnly day, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // Count-then-insert gave two patients booking at the same moment the same position, from any pod. The
        // lock is per clinic and day and ends with the transaction; it needs no change to the tables.
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({clinicId}, {day.DayNumber})", ct);

        if (!await db.Clinics.AnyAsync(c => c.Id == clinicId, ct))
        {
            return null;
        }

        var position = await db.Visits.CountAsync(v => v.ClinicId == clinicId && v.Day == day, ct) + 1;
        var visit = new Visit(clinicId, patientId, day, position);
        db.Visits.Add(visit);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        // Only this pod's cache: the cache is in memory per pod, so other pods catch up within its one minute.
        await snapshots.RemoveAsync(clinicId, day, ct);
        return visit;
    }
}
