using System.Globalization;
using ClinicReminders.Domain;
using Microsoft.EntityFrameworkCore;

namespace ClinicReminders.Infrastructure;

public sealed record ReminderRow(int Id, DateTimeOffset AppointmentAt, DateTimeOffset CreatedAt, bool Sent);

public sealed record ReminderPage(IReadOnlyList<ReminderRow> Items, string? Next);

public sealed class ReminderQueries(RemindersDbContext db)
{
    // The page after `cursor`, oldest first, for the reminders screen. `Next` is null on the last page.
    // The cursor is "<utc ticks>.<id>" of the last row: (CreatedAt, Id) is unique, so rows sharing a
    // timestamp are neither skipped nor repeated, and it survives URL encoding (no '+' or ':').
    public async Task<ReminderPage> PageAsync(int patientId, string? cursor, int size, CancellationToken ct)
    {
        var reminders = db.Reminders.Where(r => r.PatientId == patientId);
        if (cursor is not null)
        {
            if (!TryParseCursor(cursor, out var c))
            {
                throw new ArgumentException("Invalid cursor.", nameof(cursor));
            }

            var (after, afterId) = c;
            reminders = reminders.Where(r => r.CreatedAt > after || (r.CreatedAt == after && r.Id > afterId));
        }

        // One extra row tells us whether there is another page.
        var rows = await reminders
            .OrderBy(r => r.CreatedAt).ThenBy(r => r.Id)
            .Take(size + 1)
            .Select(r => new ReminderRow(r.Id, r.AppointmentAt, r.CreatedAt, r.SentAt != null))
            .ToListAsync(ct);
        var items = rows.Take(size).ToList();
        var next = rows.Count > size
            ? string.Create(CultureInfo.InvariantCulture, $"{items[^1].CreatedAt.UtcTicks}.{items[^1].Id}")
            : null;
        return new ReminderPage(items, next);
    }

    public static bool TryParseCursor(string cursor, out (DateTimeOffset At, int Id) value)
    {
        value = default;
        var parts = cursor.Split('.');
        if (parts.Length != 2
            || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || ticks > DateTimeOffset.MaxValue.UtcTicks
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var id))
        {
            return false;
        }

        value = (new DateTimeOffset(ticks, TimeSpan.Zero), id);
        return true;
    }

    public Task<int> MarkSentAsync(IReadOnlyCollection<int> ids, DateTimeOffset at, CancellationToken ct) =>
        db.Reminders.Where(r => ids.Contains(r.Id)).ExecuteUpdateAsync(set =>
        {
            set.SetProperty(r => r.SentAt, at);
        }, ct);
}
