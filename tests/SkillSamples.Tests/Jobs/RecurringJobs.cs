using Hangfire;

namespace SkillSamples.Jobs;

public interface IDailyReport
{
    Task GenerateAsync(CancellationToken ct);
}

public static class RecurringJobs
{
    public static void Register(IRecurringJobManager recurring)
    {
        // The IANA id works on Linux and Windows. "Arab Standard Time" (the Windows id) needs ICU on Linux,
        // which Alpine images don't have unless they add it.
        var riyadh = TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh");

        recurring.AddOrUpdate<IDailyReport>(
            "daily-report", "low",
            report => report.GenerateAsync(CancellationToken.None),
            Cron.Daily(2),                                   // 02:00 in Riyadh, 23:00 UTC the day before
            new RecurringJobOptions { TimeZone = riyadh });
    }
}
