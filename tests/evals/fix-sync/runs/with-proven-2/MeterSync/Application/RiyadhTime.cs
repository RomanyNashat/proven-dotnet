namespace MeterSync.Application;

/// <summary>
/// Riyadh's time zone, also on an image without tzdata (where FindSystemTimeZoneById throws). Saudi Arabia is
/// UTC+3 all year with no daylight saving, so the fixed offset is exact.
/// </summary>
public static class RiyadhTime
{
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
