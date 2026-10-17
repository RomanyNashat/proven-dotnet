using Microsoft.EntityFrameworkCore;

namespace ClinicReminders.Infrastructure;

public sealed record ReminderRow(int Id, DateTimeOffset AppointmentAt, DateTimeOffset CreatedAt, bool Sent);

public sealed record ReminderPage(IReadOnlyList<ReminderRow> Items, string? Next);

public sealed class ReminderQueries(RemindersDbContext db)
{
    /// <summary>The page after <paramref name="after"/>, oldest first. <c>Next</c> is null on the last page.</summary>
    public async Task<ReminderPage> PageAsync(int patientId, ReminderCursor? after, int size, CancellationToken ct)
    {
        var reminders = db.Reminders.Where(r => r.PatientId == patientId);
        if (after is not null)
        {
            reminders = reminders.Where(r => r.CreatedAt > after.CreatedAt || (r.CreatedAt == after.CreatedAt && r.Id > after.Id));
        }

        // One row more than the page tells whether there is a next page, so a full last page has no cursor.
        var items = await reminders
            .OrderBy(r => r.CreatedAt).ThenBy(r => r.Id)
            .Take(size + 1)
            .Select(r => new ReminderRow(r.Id, r.AppointmentAt, r.CreatedAt, r.SentAt != null))
            .ToListAsync(ct);
        if (items.Count <= size)
        {
            return new ReminderPage(items, null);
        }

        var page = items[..size];
        return new ReminderPage(page, new ReminderCursor(page[^1].CreatedAt, page[^1].Id).Encode());
    }

    /// <summary>One of the patient's reminders; someone else's is null, the same as one that doesn't exist.</summary>
    public Task<ReminderRow?> FindAsync(int patientId, int id, CancellationToken ct) =>
        db.Reminders
            .Where(r => r.Id == id && r.PatientId == patientId)
            .Select(r => new ReminderRow(r.Id, r.AppointmentAt, r.CreatedAt, r.SentAt != null))
            .SingleOrDefaultAsync(ct);
}
