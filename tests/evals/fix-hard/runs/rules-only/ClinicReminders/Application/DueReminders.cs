using ClinicReminders.Domain;
using ClinicReminders.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ClinicReminders.Application;

/// <summary>
/// Finds reminders that are due and moves each one through Pending → Sending → Sent/Failed/Unknown with
/// set-based updates, so two runners (two pods, or a rerun) can never both send the same reminder.
/// </summary>
public sealed class DueReminders(RemindersDbContext db)
{
    /// <summary>Ids of pending reminders whose appointment is in (<paramref name="now"/>, <paramref name="until"/>], after <paramref name="afterId"/>.</summary>
    /// <param name="now">The current time; appointments at or before it are skipped.</param>
    /// <param name="until">The end of the reminder window.</param>
    /// <param name="afterId">The last id already processed in this run (keyset).</param>
    /// <param name="take">The batch size.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The ids, in ascending order.</returns>
    public Task<List<int>> FindAsync(DateTimeOffset now, DateTimeOffset until, int afterId, int take, CancellationToken ct) =>
        db.Reminders
            .Where(r => r.Status == ReminderStatus.Pending
                && r.AppointmentAt > now
                && r.AppointmentAt <= until
                && r.Id > afterId)
            .OrderBy(r => r.Id)
            .Select(r => r.Id)
            .Take(take)
            .ToListAsync(ct);

    /// <summary>Claims a pending reminder for sending. The claim is committed before the SMS goes out.</summary>
    /// <param name="id">The reminder id.</param>
    /// <param name="now">The claim time.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The reminder, or null when another runner claimed it first or it is no longer pending.</returns>
    public async Task<Reminder?> TryClaimAsync(int id, DateTimeOffset now, CancellationToken ct)
    {
        var claimed = await db.Reminders
            .Where(r => r.Id == id && r.Status == ReminderStatus.Pending)
            .ExecuteUpdateAsync(set =>
            {
                set.SetProperty(r => r.Status, ReminderStatus.Sending);
                set.SetProperty(r => r.AttemptedAt, (DateTimeOffset?)now);
            }, ct);

        return claimed == 0 ? null : await db.Reminders.AsNoTracking().SingleAsync(r => r.Id == id, ct);
    }

    /// <summary>Records the outcome of a claimed reminder.</summary>
    /// <param name="id">The reminder id.</param>
    /// <param name="status">The new status (Pending puts it back for the next run).</param>
    /// <param name="sentAt">When it was sent, for <see cref="ReminderStatus.Sent"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the row is updated.</returns>
    public Task FinishAsync(int id, ReminderStatus status, DateTimeOffset? sentAt, CancellationToken ct) =>
        db.Reminders
            .Where(r => r.Id == id && r.Status == ReminderStatus.Sending)
            .ExecuteUpdateAsync(set =>
            {
                set.SetProperty(r => r.Status, status);
                set.SetProperty(r => r.SentAt, sentAt);
            }, ct);
}
