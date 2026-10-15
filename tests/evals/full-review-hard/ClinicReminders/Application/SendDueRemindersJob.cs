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
        }

        await db.SaveChangesAsync(context.CancellationToken);
    }
}
