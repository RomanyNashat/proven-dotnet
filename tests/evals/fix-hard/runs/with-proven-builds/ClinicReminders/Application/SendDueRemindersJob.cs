using Microsoft.Extensions.Logging;
using Quartz;

namespace ClinicReminders.Application;

[DisallowConcurrentExecution]   // one run at a time in this scheduler; the claims cover other pods
public sealed class SendDueRemindersJob(IServiceScopeFactory scopes, ILogger<SendDueRemindersJob> logger) : IJob
{
    public static readonly JobKey Key = new("send-due-reminders");
    public const string TriggerName = "send-due-reminders-daily";

    public async Task Execute(IJobExecutionContext context)
    {
        // Each run owns its scope, so its DbContext lives for the run and no longer, however the job is resolved.
        await using var scope = scopes.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<ReminderDispatcher>();
        try
        {
            var summary = await dispatcher.SendDueAsync(context.CancellationToken);
            ReminderLog.RunFinished(logger, summary.Sent, summary.NotSent, summary.Unknown, summary.Failed, summary.Skipped);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ReminderLog.RunFailed(logger, ex.GetType().Name);
            throw new JobExecutionException(ex, refireImmediately: false);
        }
    }
}
