using Microsoft.EntityFrameworkCore;

namespace ClinicReminders.Infrastructure;

public sealed record ReminderRow(int Id, DateTimeOffset AppointmentAt, DateTimeOffset CreatedAt, bool Sent);

public sealed record ReminderPage(IReadOnlyList<ReminderRow> Items, string? Next);

public sealed class ReminderQueries(RemindersDbContext db)
{
    // The page after `after`, oldest first, for the reminders screen. `Next` is null on the last page.
    public async Task<ReminderPage> PageAsync(int patientId, ReminderCursor? after, int size, CancellationToken ct)
    {
        var reminders = db.Reminders.Where(r => r.PatientId == patientId);
        if (after is not null)
        {
            var createdAt = after.CreatedAt;
            var lastId = after.Id;
            reminders = reminders.Where(r => r.CreatedAt > createdAt || (r.CreatedAt == createdAt && r.Id > lastId));
        }

        // One row more than the page: if it comes back there is a next page, and the last page says so (null).
        var rows = await reminders
            .OrderBy(r => r.CreatedAt).ThenBy(r => r.Id)
            .Take(size + 1)
            .Select(r => new ReminderRow(r.Id, r.AppointmentAt, r.CreatedAt, r.SentAt != null))
            .ToListAsync(ct);
        if (rows.Count <= size)
        {
            return new ReminderPage(rows, null);
        }

        var last = rows[size - 1];
        return new ReminderPage(rows.GetRange(0, size), new ReminderCursor(last.CreatedAt, last.Id).Encode());
    }

    // The patient's own reminder, or null: someone else's is indistinguishable from one that doesn't exist.
    public Task<ReminderRow?> FindAsync(int patientId, int id, CancellationToken ct) =>
        db.Reminders
            .Where(r => r.Id == id && r.PatientId == patientId)
            .Select(r => new ReminderRow(r.Id, r.AppointmentAt, r.CreatedAt, r.SentAt != null))
            .SingleOrDefaultAsync(ct);
}
