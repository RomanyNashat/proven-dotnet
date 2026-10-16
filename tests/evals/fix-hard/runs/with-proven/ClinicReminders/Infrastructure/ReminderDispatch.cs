using Microsoft.EntityFrameworkCore;

namespace ClinicReminders.Infrastructure;

public sealed record DueReminder(int Id, string Mobile, DateTimeOffset AppointmentAt);

/// <summary>
/// The send-side state of a reminder, changed with single conditional UPDATEs. The SMS provider takes no
/// idempotency key, so a reminder is claimed before it is sent and only one claim can win: an SMS goes out
/// at most once, across overlapping runs and pods. None of this runs inside a transaction with the HTTP call.
/// </summary>
public sealed class ReminderDispatch(RemindersDbContext db)
{
    public Task<List<DueReminder>> FindDueAsync(DateTimeOffset after, DateTimeOffset before, int maxAttempts, CancellationToken ct) =>
        db.Reminders
            .Where(r => r.SentAt == null && r.ClaimedAt == null && r.Attempts < maxAttempts
                        && r.AppointmentAt > after && r.AppointmentAt < before)
            .OrderBy(r => r.AppointmentAt).ThenBy(r => r.Id)
            .Select(r => new DueReminder(r.Id, r.Mobile, r.AppointmentAt))
            .ToListAsync(ct);

    /// <summary>True if this caller now owns the send; false if another run or pod claimed it first.</summary>
    public async Task<bool> TryClaimAsync(int id, DateTimeOffset at, CancellationToken ct) =>
        await db.Reminders
            .Where(r => r.Id == id && r.SentAt == null && r.ClaimedAt == null)
            .ExecuteUpdateAsync(set => set
                .SetProperty(r => r.ClaimedAt, (DateTimeOffset?)at)
                .SetProperty(r => r.Attempts, r => r.Attempts + 1), ct) == 1;

    public Task MarkSentAsync(int id, DateTimeOffset at, CancellationToken ct) =>
        db.Reminders
            .Where(r => r.Id == id)
            .ExecuteUpdateAsync(set => set.SetProperty(r => r.SentAt, (DateTimeOffset?)at), ct);

    /// <summary>Only for a send known not to have happened: the next run may try again (up to the cap).</summary>
    public Task ReleaseAsync(int id, CancellationToken ct) =>
        db.Reminders
            .Where(r => r.Id == id && r.SentAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(r => r.ClaimedAt, (DateTimeOffset?)null), ct);
}
