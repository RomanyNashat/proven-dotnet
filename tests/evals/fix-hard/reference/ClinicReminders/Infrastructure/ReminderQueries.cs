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
            // The id breaks ties: a batch import creates many reminders in the same instant.
            var parts = cursor.Split('|');
            var after = DateTimeOffset.Parse(parts[0], CultureInfo.InvariantCulture);
            var afterId = int.Parse(parts[1], CultureInfo.InvariantCulture);
            reminders = reminders.Where(r => r.CreatedAt > after || (r.CreatedAt == after && r.Id > afterId));
        }

        var items = await reminders
            .OrderBy(r => r.CreatedAt)
            .ThenBy(r => r.Id)
            .Take(size)
            .Select(r => new ReminderRow(r.Id, r.AppointmentAt, r.CreatedAt, r.SentAt != null))
            .ToListAsync(ct);
        var next = items.Count == size
            ? string.Create(CultureInfo.InvariantCulture, $"{items[^1].CreatedAt:O}|{items[^1].Id}")
            : null;
        return new ReminderPage(items, next);
    }

    /// <summary>Marks the reminder sent if no one has; 1 when this caller claimed it.</summary>
    public Task<int> ClaimAsync(int id, DateTimeOffset at, CancellationToken ct) =>
        db.Reminders.Where(r => r.Id == id && r.SentAt == null).ExecuteUpdateAsync(set =>
        {
            set.SetProperty(r => r.SentAt, at);
        }, ct);

    public Task<int> ReleaseAsync(int id, CancellationToken ct) =>
        db.Reminders.Where(r => r.Id == id).ExecuteUpdateAsync(set =>
        {
            set.SetProperty(r => r.SentAt, (DateTimeOffset?)null);
        }, ct);

    public Task<int> MarkSentAsync(IReadOnlyCollection<int> ids, DateTimeOffset at, CancellationToken ct) =>
        db.Reminders.Where(r => ids.Contains(r.Id)).ExecuteUpdateAsync(set =>
        {
            set.SetProperty(r => r.SentAt, at);
        }, ct);
}
