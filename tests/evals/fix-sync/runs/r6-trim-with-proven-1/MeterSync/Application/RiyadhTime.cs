namespace MeterSync.Application;

/// <summary>The patients' time zone.</summary>
public static class RiyadhTime
{
    private const string ZoneId = "Asia/Riyadh";

    /// <summary>Asia/Riyadh, or a fixed UTC+3 zone when the image has no tzdata.</summary>
    public static readonly TimeZoneInfo Zone = Resolve(ZoneId);

    // Saudi Arabia is UTC+3 all year with no daylight saving, so the fixed offset is exact.
    internal static TimeZoneInfo Resolve(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.CreateCustomTimeZone(ZoneId, TimeSpan.FromHours(3), "Riyadh", "Riyadh");
        }
    }
}
