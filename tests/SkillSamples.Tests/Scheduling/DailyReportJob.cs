using Microsoft.Extensions.Logging;
using Quartz;

namespace SkillSamples.Scheduling;

public interface IDailyReportBuilder
{
    Task BuildAsync(DateTimeOffset scheduledFor, CancellationToken ct);
}

[DisallowConcurrentExecution]   // a second trigger waits until this run ends (per job key, across the cluster)
public sealed class DailyReportJob(IDailyReportBuilder reports, ILogger<DailyReportJob> logger) : IJob
{
    public static readonly JobKey Key = new("daily-report");

    public async Task Execute(IJobExecutionContext context)
    {
        // The time it was meant for, not the time it ran: a misfired run still reports the right day.
        var scheduledFor = context.ScheduledFireTimeUtc ?? context.FireTimeUtc;
        try
        {
            await reports.BuildAsync(scheduledFor, context.CancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Daily report for {ScheduledFor} failed", scheduledFor);
            throw new JobExecutionException(ex, refireImmediately: false);
        }
    }
}
