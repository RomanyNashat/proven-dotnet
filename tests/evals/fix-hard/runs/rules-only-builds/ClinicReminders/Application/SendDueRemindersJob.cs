using ClinicReminders.Infrastructure;
using Microsoft.Extensions.Logging;
using Quartz;

namespace ClinicReminders.Application;

/// <summary>
/// Sends the SMS for every pending reminder whose appointment is within a day. Safe to run on several pods at once
/// and safe to run late: it works from the table, not from the trigger time, so a missed run is made up by the next.
/// </summary>
[DisallowConcurrentExecution]
public sealed class SendDueRemindersJob(DueReminders due, SmsClient sms, ILogger<SendDueRemindersJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        foreach (var reminder in await due.FindAsync(ct))
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }

            if (!await due.TryClaimAsync(reminder.Id, ct))
            {
                continue;
            }

            var outcome = await sms.SendAsync(reminder.Mobile, ReminderText.For(reminder.AppointmentAt), ct);

            // Not the run's token: when shutdown cancels the run mid-send, the outcome must still be written,
            // or a reminder that went out would look like one that did not.
            await due.RecordAsync(reminder.Id, outcome, CancellationToken.None);
            ReminderLog.Outcome(logger, reminder.Id, outcome);
        }
    }
}
