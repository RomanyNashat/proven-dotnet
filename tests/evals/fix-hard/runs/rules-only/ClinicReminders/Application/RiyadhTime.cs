namespace ClinicReminders.Application;

/// <summary>The clinic's time zone.</summary>
public static class RiyadhTime
{
    /// <summary>
    /// Asia/Riyadh. An image without tzdata has no zone database; Saudi Arabia has been UTC+3 all year with no
    /// daylight saving, so a fixed +3 zone is a correct fallback rather than a crash at startup.
    /// </summary>
    public static TimeZoneInfo Zone { get; } =
        TimeZoneInfo.TryFindSystemTimeZoneById("Asia/Riyadh", out var zone)
            ? zone
            : TimeZoneInfo.CreateCustomTimeZone("Asia/Riyadh", TimeSpan.FromHours(3), "Riyadh", "Riyadh");
}
