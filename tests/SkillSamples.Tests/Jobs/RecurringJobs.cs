using Hangfire;
using SkillSamples.Localization;

namespace SkillSamples.Jobs;

public interface IDailyReport
{
    Task GenerateAsync(CancellationToken ct);
}

public static class RecurringJobs
{
    public static void Register(IRecurringJobManager recurring)
    {
        // Not FindSystemTimeZoneById("Asia/Riyadh"): it throws on images without tzdata. RiyadhTime falls
        // back to a fixed UTC+3 zone there (`localization` §7). Windows ids need ICU on Linux as well.
        var riyadh = RiyadhTime.Zone;

        recurring.AddOrUpdate<IDailyReport>(
            "daily-report", "low",
            report => report.GenerateAsync(CancellationToken.None),
            Cron.Daily(2),                                   // 02:00 in Riyadh, 23:00 UTC the day before
            new RecurringJobOptions { TimeZone = riyadh });
    }
}
