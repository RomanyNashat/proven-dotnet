namespace MeterSync.Application;

/// <summary>Riyadh's time zone, also on an image without tzdata.</summary>
public static class RiyadhTime
{
    // Saudi Arabia is UTC+3 all year, with no daylight saving, so the fixed offset is exact. Resolved once:
    // a lookup per SMS that throws without tzdata failed every sweep.
    public static readonly TimeZoneInfo Zone = Resolve();

    private static TimeZoneInfo Resolve()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.CreateCustomTimeZone("Asia/Riyadh", TimeSpan.FromHours(3), "Riyadh", "Riyadh");
        }
    }
}
