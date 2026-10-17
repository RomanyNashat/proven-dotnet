using Quartz;

namespace ClinicReminders.Application;

public static class SchedulingSetup
{
    /// <summary>
    /// In-memory store, one scheduling replica. Quartz's persistent tables on SQL Server use unbounded and
    /// binary columns, which the column rules allow only as a recorded exception; until that decision is
    /// made, a run missed while the service is down is not caught up (see the misfire note below). A second
    /// replica would fire the job too, but sends nothing twice: every reminder is claimed in the database.
    /// </summary>
    public static IServiceCollection AddReminderScheduling(this IServiceCollection services)
    {
        services.AddTransient<SendDueRemindersJob>();
        services.AddQuartz(q =>
        {
            q.SchedulerName = "clinic-reminders";
            q.UseInMemoryStore();
            q.AddJob<SendDueRemindersJob>(job => job.WithIdentity(SendDueRemindersJob.Key).StoreDurably());

            // Every morning at 9 Riyadh time, when patients are awake (06:00 UTC; pods run in UTC).
            // FireAndProceed: a slot missed while this process was busy runs once, at once.
            q.AddTrigger(trigger => trigger
                .ForJob(SendDueRemindersJob.Key)
                .WithIdentity(SendDueRemindersJob.TriggerName)
                .WithCronSchedule("0 0 9 * * ?", cron => cron
                    .InTimeZone(RiyadhTime.Zone)
                    .WithMisfireHandlingInstructionFireAndProceed()));
        });

        services.AddQuartzHostedService(o =>
        {
            o.WaitForJobsToComplete = true;
            o.AwaitApplicationStarted = true;
        });
        return services;
    }
}
