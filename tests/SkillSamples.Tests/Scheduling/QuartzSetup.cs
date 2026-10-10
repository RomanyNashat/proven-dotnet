using Microsoft.Extensions.DependencyInjection;
using Quartz;
using SkillSamples.Localization;

namespace SkillSamples.Scheduling;

public static class QuartzSetup
{
    public static IServiceCollection AddScheduling(this IServiceCollection services, string connectionString)
    {
        services.AddQuartz(q =>
        {
            q.SchedulerName = "orders-scheduler";   // the same on every instance of the service
            q.SchedulerId = "AUTO";                  // a different id per instance

            q.UsePersistentStore(store =>
            {
                store.UsePostgres(connectionString);  // tables from Quartz's script, applied by the pipeline
                store.UseSystemTextJsonSerializer();
                store.UseClustering(cluster =>
                {
                    cluster.CheckinInterval = TimeSpan.FromSeconds(15);
                    cluster.CheckinMisfireThreshold = TimeSpan.FromSeconds(30);
                });
            });
            q.UseDefaultThreadPool(pool => pool.MaxConcurrency = 10);

            // Cron runs in the scheduler's local time zone unless told otherwise, and pods run in UTC.
            // RiyadhTime falls back to a fixed UTC+3 zone on images without tzdata (`localization` §7).
            var riyadh = RiyadhTime.Zone;
            q.AddJob<DailyReportJob>(job => job.WithIdentity(DailyReportJob.Key).StoreDurably());
            q.AddTrigger(trigger => trigger
                .ForJob(DailyReportJob.Key)
                .WithIdentity("daily-report-trigger")
                .WithCronSchedule("0 0 2 * * ?", cron => cron
                    .InTimeZone(riyadh)                          // 02:00 Riyadh = 23:00 UTC
                    .WithMisfireHandlingInstructionFireAndProceed()));   // missed while down → run once on start
        });

        // Every start re-registers the trigger above and, by default, replaces the stored one: its next
        // fire time is worked out from now, so a run missed while the service was down is silently
        // dropped. Scheduling it from the stored trigger's last run keeps the missed run for the misfire rule.
        services.Configure<QuartzOptions>(options => options.Scheduling.ScheduleTriggerRelativeToReplacedTrigger = true);

        services.AddQuartzHostedService(options =>
        {
            options.WaitForJobsToComplete = true;
            options.AwaitApplicationStarted = true;
        });
        return services;
    }
}
