using ClinicQueue.Domain;
using ClinicQueue.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace ClinicQueue.Application;

/// <summary>Books a patient into a clinic's queue for a day and hands out the next position.</summary>
public sealed class VisitBooking(QueueDbContext db, IDistributedCache cache)
{
    /// <summary>Books the visit.</summary>
    /// <returns>The new visit, or null when the clinic doesn't exist.</returns>
    public async Task<Visit?> BookAsync(int clinicId, int patientId, DateTime day, CancellationToken ct)
    {
        if (!await db.Clinics.AnyAsync(c => c.Id == clinicId, ct))
        {
            return null;
        }

        // Count + 1 raced: two bookings at once both got the same position. A transaction-scoped advisory
        // lock per clinic and day makes them take turns; it is released at commit, so it can't leak onto a
        // pooled connection, and it needs no schema change.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var dayNumber = DateOnly.FromDateTime(day).DayNumber;
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({clinicId}, {dayNumber})", ct);

        var position = await db.Visits.CountAsync(v => v.ClinicId == clinicId && v.Day == day.Date, ct) + 1;
        var visit = new Visit(clinicId, patientId, day, position);
        db.Visits.Add(visit);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        await cache.RemoveAsync(QueueSnapshots.KeyFor(clinicId, day), ct);
        return visit;
    }
}
