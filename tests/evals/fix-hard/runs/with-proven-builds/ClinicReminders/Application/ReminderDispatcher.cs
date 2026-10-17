using ClinicReminders.Domain;
using ClinicReminders.Infrastructure;
using Microsoft.Extensions.Logging;

namespace ClinicReminders.Application;

public sealed record DispatchSummary(int Sent, int NotSent, int Unknown, int Failed, int Skipped);

/// <summary>
/// Sends the reminders due in the next 24 hours, at most once each. The provider has no idempotency key,
/// so every reminder is claimed in the database before the call: claim, send, record. A refusal releases
/// the claim for the next run; a timeout or a crash leaves it claimed, and the reminder is not sent again.
/// </summary>
public sealed class ReminderDispatcher(IDueReminders reminders, SmsClient sms, TimeProvider time, ILogger<ReminderDispatcher> logger)
{
    public const int BatchSize = 200;

    public async Task<DispatchSummary> SendDueAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var window = new DueWindow(now, now + Reminder.SendAhead);   // past appointments are never texted
        int sent = 0, notSent = 0, unknown = 0, failed = 0, skipped = 0;

        var afterId = 0;
        while (true)
        {
            var batch = await reminders.FindAsync(window, afterId, BatchSize, ct);
            foreach (var reminder in batch)
            {
                ct.ThrowIfCancellationRequested();
                switch (await SendOneAsync(reminder, ct))
                {
                    case Outcome.Sent: sent++; break;
                    case Outcome.NotSent: notSent++; break;
                    case Outcome.Unknown: unknown++; break;
                    case Outcome.Failed: failed++; break;
                    case Outcome.Skipped: skipped++; break;
                }
            }

            if (batch.Count < BatchSize)
            {
                break;
            }

            afterId = batch[^1].Id;   // released rows have lower ids, so a run tries each reminder once
        }

        return new DispatchSummary(sent, notSent, unknown, failed, skipped);
    }

    private async Task<Outcome> SendOneAsync(DueReminder reminder, CancellationToken ct)
    {
        try
        {
            // The text first: if it can't be built, nothing is claimed.
            var text = ReminderMessage.Arabic(reminder.AppointmentAt);
            if (!await reminders.TryClaimAsync(reminder.Id, time.GetUtcNow(), ct))
            {
                return Outcome.Skipped;   // another run has it
            }

            var result = await sms.SendAsync(reminder.Mobile, text, ct);
            switch (result.Delivery)
            {
                case SmsDelivery.Sent:
                    // Recorded even if the service is stopping: the message is out.
                    await reminders.MarkSentAsync(reminder.Id, time.GetUtcNow(), CancellationToken.None);
                    ReminderLog.Sent(logger, reminder.Id, reminder.Mobile);
                    return Outcome.Sent;
                case SmsDelivery.NotSent:
                    await reminders.ReleaseAsync(reminder.Id, CancellationToken.None);
                    ReminderLog.NotSent(logger, reminder.Id, result.StatusCode, result.Failure);
                    return Outcome.NotSent;
                default:
                    ReminderLog.OutcomeUnknown(logger, reminder.Id, result.Failure);
                    return Outcome.Unknown;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // The type only: an exception's message can carry the mobile or the text.
            ReminderLog.Failed(logger, reminder.Id, ex.GetType().Name);
            return Outcome.Failed;
        }
    }

    private enum Outcome
    {
        Sent,
        NotSent,
        Unknown,
        Failed,
        Skipped,
    }
}
