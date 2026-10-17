namespace ClinicReminders.Application;

public static class ClinicTime
{
    /// <summary>
    /// The clinic's zone. Saudi Arabia is UTC+3 all year with no daylight saving, so when the machine has no
    /// tzdata (slim container images) a fixed UTC+3 zone is the same clock. Without this fallback a missing
    /// tzdata throws on first use and nothing is sent.
    /// </summary>
    public static TimeZoneInfo Riyadh { get; } =
        TimeZoneInfo.TryFindSystemTimeZoneById("Asia/Riyadh", out var zone)
            ? zone
            : TimeZoneInfo.CreateCustomTimeZone("Asia/Riyadh", TimeSpan.FromHours(3), "Riyadh", "Riyadh");

    public static DateTimeOffset ToRiyadh(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, Riyadh);
}
