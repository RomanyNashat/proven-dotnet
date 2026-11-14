using System.Globalization;
using ClinicQueue.Domain;
using ClinicQueue.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace ClinicQueue.Application;

/// <summary>Books a patient into a clinic's queue for a day.</summary>
public sealed class VisitBookings(QueueDbContext db, IDistributedCache cache)
{
    /// <summary>
    /// Books the next position. Bookings for one clinic and day take turns under a transaction-scoped
    /// advisory lock, so two at once can't get the same position (the table has no unique constraint).
    /// </summary>
    /// <param name="clinicId">The clinic.</param>
    /// <param name="patientId">The caller.</param>
    /// <param name="day">The day.</param>
    /// <param name="ct">Cancels the booking.</param>
    /// <returns>The visit, or null when the clinic doesn't exist.</returns>
    public async Task<Visit?> BookAsync(int clinicId, int patientId, DateOnly day, CancellationToken ct)
    {
        if (!await db.Clinics.AnyAsync(c => c.Id == clinicId, ct))
        {
            return null;
        }

        // Read committed on purpose: the MAX below must see a booking committed while this one waited for the lock.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({clinicId}, {day.DayNumber})", ct);

        // MAX, not COUNT: a deleted visit would otherwise hand its count to a new booking and repeat a position.
        var position = (await db.Visits
            .Where(v => v.ClinicId == clinicId && v.Day == day)
            .MaxAsync(v => (int?)v.Position, ct) ?? 0) + 1;
        var visit = new Visit(clinicId, patientId, day, position);
        db.Visits.Add(visit);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        // The cache is per pod, so this only refreshes this pod's screens at once; others catch up within a minute.
        await cache.RemoveAsync(QueueSnapshots.CacheKey(clinicId, day), ct);
        return visit;
    }
}
