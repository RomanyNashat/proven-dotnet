using ClinicReminders.Domain;
using Microsoft.EntityFrameworkCore;

namespace ClinicReminders.Infrastructure;

public sealed record ReminderRow(int Id, DateTimeOffset AppointmentAt, DateTimeOffset CreatedAt, bool Sent);

public sealed record ReminderPage(IReadOnlyList<ReminderRow> Items, string? Next);

public sealed class ReminderQueries(RemindersDbContext db)
{
    /// <summary>
    /// One patient's reminders after <paramref name="after"/>, oldest first. <see cref="ReminderPage.Next"/> is null
    /// on the last page.
    /// </summary>
    /// <remarks>
    /// The key is (CreatedAt, Id), not CreatedAt alone: two reminders created in the same millisecond would
    /// otherwise straddle a page boundary and one of them would never be shown.
    /// </remarks>
    public async Task<ReminderPage> PageAsync(int patientId, ReminderCursor? after, int size, CancellationToken ct)
    {
        var reminders = db.Reminders.Where(r => r.PatientId == patientId);
        if (after is { } c)
        {
            reminders = reminders.Where(r => r.CreatedAt > c.CreatedAt || (r.CreatedAt == c.CreatedAt && r.Id > c.Id));
        }

        // One extra row tells us whether another page exists, so a full last page still gets Next = null.
        var rows = await reminders
            .OrderBy(r => r.CreatedAt)
            .ThenBy(r => r.Id)
            .Take(size + 1)
            .Select(r => new ReminderRow(r.Id, r.AppointmentAt, r.CreatedAt, r.Status == ReminderStatus.Sent))
            .ToListAsync(ct);

        if (rows.Count <= size)
        {
            return new ReminderPage(rows, null);
        }

        var page = rows.GetRange(0, size);
        return new ReminderPage(page, new ReminderCursor(page[^1].CreatedAt, page[^1].Id).Encode());
    }
}
