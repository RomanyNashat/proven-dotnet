using ClinicReminders.Infrastructure;
using Quartz;

namespace ClinicReminders.Application;

[DisallowConcurrentExecution]
public sealed class SendDueRemindersJob(DueReminders due, ReminderService reminders, ReminderQueries queries, TimeProvider time) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        foreach (var reminder in await due.TakeAsync(ct))
        {
            // Claim before sending: a crash after the send can't send it again. The provider has no
            // idempotency key, so at most once is the safe side for an SMS.
            if (await queries.ClaimAsync(reminder.Id, time.GetUtcNow(), ct) == 0)
            {
                continue;
            }

            if (!await reminders.SendAsync(reminder, ct))
            {
                await queries.ReleaseAsync(reminder.Id, ct);
            }
        }
    }
}
