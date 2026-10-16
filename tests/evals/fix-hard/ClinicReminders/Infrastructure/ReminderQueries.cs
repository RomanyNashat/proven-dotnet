using System.Globalization;
using ClinicReminders.Domain;
using Microsoft.EntityFrameworkCore;

namespace ClinicReminders.Infrastructure;

public sealed record ReminderRow(int Id, DateTimeOffset AppointmentAt, DateTimeOffset CreatedAt, bool Sent);

public sealed record ReminderPage(IReadOnlyList<ReminderRow> Items, string? Next);

public sealed class ReminderQueries(RemindersDbContext db)
{
    // The page after `cursor`, oldest first, for the reminders screen. `Next` is null on the last page.
    public async Task<ReminderPage> PageAsync(int patientId, string? cursor, int size, CancellationToken ct)
    {
        var reminders = db.Reminders.Where(r => r.PatientId == patientId);
        if (cursor is not null)
        {
            var after = DateTimeOffset.Parse(cursor, CultureInfo.InvariantCulture);
            reminders = reminders.Where(r => r.CreatedAt > after);
        }

        var items = await reminders
            .OrderBy(r => r.CreatedAt)
            .Take(size)
            .Select(r => new ReminderRow(r.Id, r.AppointmentAt, r.CreatedAt, r.SentAt != null))
            .ToListAsync(ct);
        var next = items.Count == size ? items[^1].CreatedAt.ToString("O", CultureInfo.InvariantCulture) : null;
        return new ReminderPage(items, next);
    }

    public Task<int> MarkSentAsync(IReadOnlyCollection<int> ids, DateTimeOffset at, CancellationToken ct) =>
        db.Reminders.Where(r => ids.Contains(r.Id)).ExecuteUpdateAsync(set =>
        {
            set.SetProperty(r => r.SentAt, at);
        }, ct);
}
