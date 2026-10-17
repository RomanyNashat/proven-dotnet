using ClinicReminders.Application;
using ClinicReminders.Domain;
using Microsoft.EntityFrameworkCore;

namespace ClinicReminders.Infrastructure;

/// <summary>
/// Each change is one conditional UPDATE, written at once: nothing waits for a SaveChanges at the end of
/// the run, so a crash or a timeout half-way through loses no record of what went out.
/// </summary>
public sealed class DueReminderStore(RemindersDbContext db) : IDueReminders
{
    public async Task<IReadOnlyList<DueReminder>> FindAsync(DueWindow window, int afterId, int size, CancellationToken ct) =>
        await db.Reminders
            .Where(r => r.Id > afterId
                        && r.SentAt == null
                        && r.ClaimedAt == null
                        && r.Attempts < Reminder.MaxSendAttempts
                        && r.AppointmentAt > window.From
                        && r.AppointmentAt < window.Until)
            .OrderBy(r => r.Id)
            .Take(size)
            .Select(r => new DueReminder(r.Id, r.Mobile, r.AppointmentAt))
            .ToListAsync(ct);

    // Two runs (two pods, a manual trigger) can find the same row; only the one whose UPDATE changes it sends.
    public async Task<bool> TryClaimAsync(int id, DateTimeOffset now, CancellationToken ct) =>
        await db.Reminders
            .Where(r => r.Id == id && r.SentAt == null && r.ClaimedAt == null && r.Attempts < Reminder.MaxSendAttempts)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.ClaimedAt, (DateTimeOffset?)now)
                .SetProperty(r => r.Attempts, r => r.Attempts + 1), ct) == 1;

    public Task MarkSentAsync(int id, DateTimeOffset at, CancellationToken ct) =>
        db.Reminders
            .Where(r => r.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.SentAt, (DateTimeOffset?)at), ct);

    public Task ReleaseAsync(int id, CancellationToken ct) =>
        db.Reminders
            .Where(r => r.Id == id && r.SentAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.ClaimedAt, (DateTimeOffset?)null), ct);
}
