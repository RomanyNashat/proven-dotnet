namespace ClinicReminders.Application;

// Saudi Arabia is UTC+3 all year, with no daylight saving, so a fixed offset is exact. Images without tzdata
// (Alpine, chiseled) throw on the lookup; this falls back instead. One zone for the SMS text and the trigger.
public static class RiyadhTime
{
    public static readonly TimeZoneInfo Zone = Resolve();

    private static TimeZoneInfo Resolve()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.CreateCustomTimeZone("Asia/Riyadh", TimeSpan.FromHours(3), "Riyadh", "Riyadh"); }
    }
}
