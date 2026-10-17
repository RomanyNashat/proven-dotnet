using ClinicReminders.Infrastructure;
using Quartz;

namespace ClinicReminders.Application;

[DisallowConcurrentExecution]
public sealed class SendDueRemindersJob(DueReminders due, ReminderService reminders, RemindersDbContext db) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        foreach (var reminder in await due.TakeAsync(context.CancellationToken))
        {
            await reminders.SendAsync(reminder, context.CancellationToken);
            // Persist each send straight away: the SMS provider has no idempotency key, so a crash
            // later in the batch must not lose the record of messages already sent (and re-send them).
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }
}
