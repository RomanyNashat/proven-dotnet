using System.Globalization;
using ClinicReminders.Infrastructure;
using Microsoft.Extensions.Logging;
using Quartz;

namespace ClinicReminders.Application;

/// <summary>Sends the SMS for every appointment in the next 24 hours, at most once each.</summary>
[DisallowConcurrentExecution]
public sealed class SendDueRemindersJob(
    ReminderDispatch dispatch, SmsClient sms, TimeProvider time, ILogger<SendDueRemindersJob> logger) : IJob
{
    public const int MaxAttempts = 3;
    public static readonly JobKey Key = new("send-due-reminders");
    private static readonly TimeSpan Window = TimeSpan.FromDays(1);

    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        var now = time.GetUtcNow();

        // Only appointments still ahead: a reminder for one that has passed is noise.
        foreach (var reminder in await dispatch.FindDueAsync(now, now + Window, MaxAttempts, ct))
        {
            ct.ThrowIfCancellationRequested();
            await SendOnceAsync(reminder, ct);
        }
    }

    // Claim, send, record. The provider has no idempotency key, so a send whose outcome is unknown is never
    // repeated: a missed reminder is better than two. A send known not to have happened is released.
    private async Task SendOnceAsync(DueReminder reminder, CancellationToken ct)
    {
        if (!await dispatch.TryClaimAsync(reminder.Id, time.GetUtcNow(), ct))
        {
            return;   // another run or pod has it
        }

        try
        {
            await sms.SendAsync(reminder.Mobile, ReminderText.For(reminder.AppointmentAt), ct);
        }
        catch (HttpRequestException ex) when (WasNotSent(ex))
        {
            await dispatch.ReleaseAsync(reminder.Id, ct);
            ReminderLog.NotSent(logger, reminder.Id, Reason(ex));
            return;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            // A timeout (TaskCanceledException), a connection cut mid-request, or shutdown: the provider may
            // have sent it. Leave it claimed.
            ReminderLog.OutcomeUnknown(logger, reminder.Id, ex.GetType().Name);
            if (ct.IsCancellationRequested)
            {
                throw;
            }

            return;
        }

        // The SMS has gone: record it even if the service is stopping now.
        await dispatch.MarkSentAsync(reminder.Id, time.GetUtcNow(), CancellationToken.None);
        ReminderLog.Sent(logger, reminder.Id, reminder.Mobile);
    }

    // The provider answered with an error status (it refused the message), or the request never left:
    // the name didn't resolve, or the connection or TLS handshake failed.
    private static bool WasNotSent(HttpRequestException ex) =>
        ex.StatusCode is not null
        || ex.HttpRequestError is HttpRequestError.NameResolutionError
            or HttpRequestError.ConnectionError
            or HttpRequestError.SecureConnectionError;

    private static string Reason(HttpRequestException ex) =>
        ex.StatusCode is { } status
            ? ((int)status).ToString(CultureInfo.InvariantCulture)
            : ex.HttpRequestError.ToString();
}
