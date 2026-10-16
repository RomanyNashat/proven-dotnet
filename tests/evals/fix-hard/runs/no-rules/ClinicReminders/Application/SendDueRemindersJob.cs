using ClinicReminders.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Quartz;

namespace ClinicReminders.Application;

[DisallowConcurrentExecution]
public sealed class SendDueRemindersJob(
    ReminderService reminders, RemindersDbContext db, TimeProvider time, ILogger<SendDueRemindersJob> logger) : IJob
{
    private const int BatchSize = 1000;

    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        var now = time.GetUtcNow();
        var until = now.AddDays(1);

        // Only upcoming appointments: a reminder for one that already happened is noise.
        var due = await db.Reminders.AsNoTracking()
            .Where(r => r.SentAt == null && r.AppointmentAt > now && r.AppointmentAt < until)
            .OrderBy(r => r.AppointmentAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        foreach (var reminder in due)
        {
            try
            {
                await reminders.SendAsync(reminder, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One bad reminder must not stop the rest of the batch.
                logger.LogError(ex, "Reminder {ReminderId} failed unexpectedly", reminder.Id);
            }
        }
    }
}
