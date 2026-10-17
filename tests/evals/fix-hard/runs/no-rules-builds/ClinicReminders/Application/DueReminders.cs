using ClinicReminders.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ClinicReminders.Application;

/// <summary>Finds the unsent reminders for appointments today (still ahead) and tomorrow, Riyadh time.</summary>
public sealed class DueReminders(RemindersDbContext db, TimeProvider time)
{
    public async Task<List<Domain.Reminder>> TakeAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var localNow = TimeZoneInfo.ConvertTime(now, ReminderService.Riyadh);
        // Riyadh has no DST, so local midnight is a fixed offset from UTC.
        var until = new DateTimeOffset(localNow.Date.AddDays(2), localNow.Offset);
        return await db.Reminders
            .Where(r => r.SentAt == null && r.AppointmentAt > now && r.AppointmentAt < until)
            .OrderBy(r => r.AppointmentAt).ThenBy(r => r.Id)
            .ToListAsync(ct);
    }
}
