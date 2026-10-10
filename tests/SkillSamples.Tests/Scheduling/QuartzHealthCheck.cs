using Microsoft.Extensions.Diagnostics.HealthChecks;
using Quartz;

namespace SkillSamples.Scheduling;

public sealed class QuartzHealthCheck(ISchedulerFactory schedulerFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        var scheduler = await schedulerFactory.GetScheduler(ct);
        if (!scheduler.IsStarted || scheduler.InStandbyMode || scheduler.IsShutdown)
            return HealthCheckResult.Unhealthy("Scheduler is not running");

        var metadata = await scheduler.GetMetaData(ct);
        return HealthCheckResult.Healthy($"Running on {metadata.SchedulerInstanceId}, {metadata.NumberOfJobsExecuted} jobs run");
    }
}
