using System.Globalization;
using ClinicReminders.Domain;
using Microsoft.EntityFrameworkCore;

namespace ClinicReminders.Infrastructure;

public sealed record ReminderRow(int Id, DateTimeOffset AppointmentAt, DateTimeOffset CreatedAt, bool Sent);

public sealed record ReminderPage(IReadOnlyList<ReminderRow> Items, string? Next);

public sealed class ReminderQueries(RemindersDbContext db)
{
    // The page after `cursor`, oldest first, for the reminders screen. `Next` is null on the last page.
    // The cursor is "{createdAtUtcTicks}-{id}" (URL-safe); ordering and paging use (CreatedAt, Id) so rows
    // sharing a timestamp are neither skipped nor repeated. Returns null if the cursor is malformed.
    public async Task<ReminderPage?> PageAsync(int patientId, string? cursor, int size, CancellationToken ct)
    {
        IQueryable<Reminder> reminders = db.Reminders.AsNoTracking().Where(r => r.PatientId == patientId);
        if (!string.IsNullOrEmpty(cursor))
        {
            var parts = cursor.Split('-');
            if (parts.Length != 2
                || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var afterId)
                || ticks > DateTimeOffset.MaxValue.UtcTicks)
            {
                return null;
            }

            var after = new DateTimeOffset(ticks, TimeSpan.Zero);
            reminders = reminders.Where(r => r.CreatedAt > after || (r.CreatedAt == after && r.Id > afterId));
        }

        var items = await reminders
            .OrderBy(r => r.CreatedAt).ThenBy(r => r.Id)
            .Take(size + 1)
            .Select(r => new ReminderRow(r.Id, r.AppointmentAt, r.CreatedAt, r.SentAt != null))
            .ToListAsync(ct);

        if (items.Count <= size)
        {
            return new ReminderPage(items, null);
        }

        items.RemoveAt(items.Count - 1);
        var last = items[^1];
        return new ReminderPage(items, string.Create(CultureInfo.InvariantCulture, $"{last.CreatedAt.UtcTicks}-{last.Id}"));
    }
}
