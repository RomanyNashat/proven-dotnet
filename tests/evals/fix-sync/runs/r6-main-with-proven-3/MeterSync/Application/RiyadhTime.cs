namespace MeterSync.Application;

/// <summary>The patients' time zone.</summary>
public static class RiyadhTime
{
    // Saudi Arabia is UTC+3 all year with no daylight saving, so a fixed offset is exact
    // on images that ship without tzdata.
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
