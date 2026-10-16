namespace SkillSamples.CronJobs;

public static class ReportDay
{
    /// <summary>
    /// The day before today in the business's time zone. A job scheduled for 02:00 Riyadh runs at 23:00 UTC
    /// the evening before, so "yesterday" worked out in UTC is the day before yesterday.
    /// </summary>
    public static DateOnly Yesterday(TimeProvider time, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), zone).DateTime).AddDays(-1);
}
