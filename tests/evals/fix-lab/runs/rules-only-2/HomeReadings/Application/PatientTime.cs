namespace HomeReadings.Application;

/// <summary>
/// Patients are in Riyadh (UTC+3, no daylight saving). Times are stored in UTC; "today" is the patient's day.
/// </summary>
public static class PatientTime
{
    public const string TimeZoneId = "Asia/Riyadh";

    // A slim image may have no tzdata; Riyadh has been a fixed UTC+3 for decades, so the fallback is exact.
    public static readonly TimeZoneInfo Zone = TimeZoneInfo.TryFindSystemTimeZoneById(TimeZoneId, out var zone)
        ? zone
        : TimeZoneInfo.CreateCustomTimeZone(TimeZoneId, TimeSpan.FromHours(3), "Riyadh", "Riyadh");

    /// <summary>
    /// A time from the app in UTC. A time with an offset or a Z is exact; one without is the patient's wall clock.
    /// Truncated to microseconds, PostgreSQL's precision, so the same reading sent twice compares equal.
    /// </summary>
    public static DateTime ToUtc(DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => TimeZoneInfo.ConvertTimeToUtc(value, Zone),
        };
        return utc.AddTicks(-(utc.Ticks % (TimeSpan.TicksPerMillisecond / 1000)));
    }

    /// <summary>The patient's current day as a UTC range [start, end).</summary>
    public static (DateTime StartUtc, DateTime EndUtc) Today(DateTimeOffset now)
    {
        var localDate = TimeZoneInfo.ConvertTime(now, Zone).Date;
        return (TimeZoneInfo.ConvertTimeToUtc(localDate, Zone), TimeZoneInfo.ConvertTimeToUtc(localDate.AddDays(1), Zone));
    }
}
