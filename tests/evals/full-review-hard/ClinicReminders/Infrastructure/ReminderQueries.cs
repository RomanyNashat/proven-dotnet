using ClinicReminders.Domain;
using Microsoft.EntityFrameworkCore;

namespace ClinicReminders.Infrastructure;

public sealed record ReminderRow(int Id, DateTimeOffset AppointmentAt, DateTimeOffset CreatedAt, bool Sent);

public sealed class ReminderQueries(RemindersDbContext db)
{
    // The page after `after`, oldest first, for the reminders screen.
    public Task<List<ReminderRow>> PageAsync(int patientId, DateTimeOffset? after, int size, CancellationToken ct)
    {
        var reminders = db.Reminders.Where(r => r.PatientId == patientId);
        if (after is not null)
        {
            reminders = reminders.Where(r => r.CreatedAt > after);
        }

        return reminders
            .OrderBy(r => r.CreatedAt)
            .Take(size)
            .Select(r => new ReminderRow(r.Id, r.AppointmentAt, r.CreatedAt, r.SentAt != null))
            .ToListAsync(ct);
    }

    public Task<int> MarkSentAsync(IReadOnlyCollection<int> ids, DateTimeOffset at, CancellationToken ct) =>
        db.Reminders.Where(r => ids.Contains(r.Id)).ExecuteUpdateAsync(set =>
        {
            set.SetProperty(r => r.SentAt, at);
        }, ct);
}
