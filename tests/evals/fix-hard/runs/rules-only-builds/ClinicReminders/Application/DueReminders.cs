using ClinicReminders.Domain;
using ClinicReminders.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ClinicReminders.Application;

public sealed record DueReminder(int Id, string Mobile, DateTimeOffset AppointmentAt);

/// <summary>
/// The send job's view of the reminders table. Every state change is its own committed statement, so a run that
/// dies part-way keeps what it already did. Scoped, like the DbContext it uses.
/// </summary>
public sealed class DueReminders(RemindersDbContext db, TimeProvider time)
{
    /// <summary>A reminder goes out once its appointment is this close.</summary>
    public static readonly TimeSpan Lead = TimeSpan.FromDays(1);

    private const int BatchSize = 500;

    /// <summary>Pending reminders for appointments in the next <see cref="Lead"/>. Past appointments are never sent.</summary>
    public async Task<IReadOnlyList<DueReminder>> FindAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var until = now + Lead;
        return await db.Reminders
            .Where(r => r.Status == ReminderStatus.Pending && r.AppointmentAt > now && r.AppointmentAt <= until)
            .OrderBy(r => r.AppointmentAt)
            .Take(BatchSize)
            .Select(r => new DueReminder(r.Id, r.Mobile, r.AppointmentAt))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Takes the reminder for this run. False when another run (another pod, or an overlapping run) already took it.
    /// Committed before the SMS is sent, so no two runs send the same reminder.
    /// </summary>
    public async Task<bool> TryClaimAsync(int id, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var claimed = await db.Reminders
            .Where(r => r.Id == id && r.Status == ReminderStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, ReminderStatus.Sending)
                .SetProperty(r => r.ClaimedAt, now), ct);
        return claimed == 1;
    }

    /// <summary>Records what the provider did with a claimed reminder.</summary>
    public Task RecordAsync(int id, SmsOutcome outcome, CancellationToken ct)
    {
        var claimed = db.Reminders.Where(r => r.Id == id && r.Status == ReminderStatus.Sending);
        var now = time.GetUtcNow();
        return outcome switch
        {
            SmsOutcome.Sent => claimed.ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, ReminderStatus.Sent)
                .SetProperty(r => r.SentAt, now), ct),

            // Never reached the provider: release it for the next run.
            SmsOutcome.NotSent => claimed.ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, ReminderStatus.Pending)
                .SetProperty(r => r.ClaimedAt, (DateTimeOffset?)null), ct),

            SmsOutcome.Rejected => claimed.ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, ReminderStatus.Rejected), ct),

            _ => claimed.ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, ReminderStatus.Unknown), ct),
        };
    }
}
