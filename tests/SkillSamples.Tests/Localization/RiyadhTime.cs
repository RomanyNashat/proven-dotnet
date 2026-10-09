namespace SkillSamples.Localization;

// Saudi Arabia is UTC+3 all year, with no daylight saving, so a fixed offset is exact.
public static class RiyadhTime
{
    public static readonly TimeZoneInfo Zone = Resolve();

    private static TimeZoneInfo Resolve()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.CreateCustomTimeZone("Asia/Riyadh", TimeSpan.FromHours(3), "Riyadh", "Riyadh"); }
    }
}
