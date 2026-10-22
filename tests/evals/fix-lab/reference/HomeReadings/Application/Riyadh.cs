namespace HomeReadings.Application;

/// <summary>The patients' time zone. Saudi Arabia has no daylight saving, so a fixed UTC+3 is the same zone
/// on an image without a time-zone database.</summary>
public static class Riyadh
{
    public static readonly TimeZoneInfo Zone = Find();

    private static TimeZoneInfo Find()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh");
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.CreateCustomTimeZone("Asia/Riyadh", TimeSpan.FromHours(3), "Riyadh", "Riyadh");
        }
    }
}
