using ClinicReminders.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ClinicReminders.Application;

/// <summary>Keeps the next day's reminders in memory so the job doesn't query on every run.</summary>
public sealed class DueReminders(RemindersDbContext db, TimeProvider time)
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<List<Domain.Reminder>> TakeAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var until = time.GetUtcNow().AddDays(1);
            return await db.Reminders.Where(r => r.SentAt == null && r.AppointmentAt < until).ToListAsync(ct);
        }
        finally
        {
            _lock.Release();
        }
    }
}
