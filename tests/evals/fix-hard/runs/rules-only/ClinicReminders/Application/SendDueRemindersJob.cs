using ClinicReminders.Domain;
using ClinicReminders.Infrastructure;
using Microsoft.Extensions.Logging;
using Quartz;

namespace ClinicReminders.Application;

/// <summary>Sends an SMS for every pending reminder whose appointment is in the next 24 hours.</summary>
[DisallowConcurrentExecution]
public sealed class SendDueRemindersJob(
    DueReminders due, SmsClient sms, TimeProvider time, ILogger<SendDueRemindersJob> logger) : IJob
{
    private const int BatchSize = 100;
    private static readonly TimeSpan Window = TimeSpan.FromDays(1);

    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        var now = time.GetUtcNow();
        var afterId = 0;
        while (true)
        {
            var ids = await due.FindAsync(now, now + Window, afterId, BatchSize, ct);
            if (ids.Count == 0)
            {
                return;
            }

            foreach (var id in ids)
            {
                await SendAsync(id, ct);
            }

            afterId = ids[^1];
        }
    }

    private async Task SendAsync(int id, CancellationToken ct)
    {
        // The claim is committed before the provider is called. With no idempotency key at the provider, a
        // reminder must not go out twice: not from a second pod, and not from a rerun after a crash.
        var reminder = await due.TryClaimAsync(id, time.GetUtcNow(), ct);
        if (reminder is null)
        {
            return;
        }

        SmsResult result;
        try
        {
            result = await sms.SendAsync(reminder.Mobile, ReminderText.ForAppointment(reminder.AppointmentAt), ct);
        }
        catch (OperationCanceledException ex) when (ct.IsCancellationRequested)
        {
            // Shutting down mid-request: the provider may have it.
            ReminderLog.OutcomeUnknown(logger, id, null, ex);
            await due.FinishAsync(id, ReminderStatus.Unknown, null, CancellationToken.None);
            throw;
        }

        switch (result.Outcome)
        {
            case SmsOutcome.Sent:
                await due.FinishAsync(id, ReminderStatus.Sent, time.GetUtcNow(), ct);
                ReminderLog.Sent(logger, id, reminder.Mobile);
                break;
            case SmsOutcome.NotSent:
                await due.FinishAsync(id, ReminderStatus.Pending, null, ct);
                ReminderLog.NotSent(logger, id, result.StatusCode, result.Error);
                break;
            case SmsOutcome.Rejected:
                await due.FinishAsync(id, ReminderStatus.Failed, null, ct);
                ReminderLog.Rejected(logger, id, result.StatusCode);
                break;
            default:
                await due.FinishAsync(id, ReminderStatus.Unknown, null, ct);
                ReminderLog.OutcomeUnknown(logger, id, result.StatusCode, result.Error);
                break;
        }
    }
}
