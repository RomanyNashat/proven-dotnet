namespace HomeReadings.Application;

/// <summary>Patients are in Riyadh: "today" is the Riyadh day, not the UTC one.</summary>
public static class RiyadhTime
{
    /// <summary>Asia/Riyadh, or a fixed UTC+3 where the image has no tzdata. Saudi Arabia has no daylight saving, so the offset is exact.</summary>
    public static readonly TimeZoneInfo Zone = Resolve();

    /// <summary>The Riyadh day containing <paramref name="now"/>, as a UTC range for a timestamptz column.</summary>
    /// <param name="now">The instant, from TimeProvider.</param>
    /// <returns>Start (inclusive) and end (exclusive), both of Kind Utc.</returns>
    public static (DateTime Start, DateTime End) DayInUtc(DateTimeOffset now)
    {
        var local = TimeZoneInfo.ConvertTime(now, Zone);
        var start = new DateTimeOffset(local.Date, local.Offset).UtcDateTime;
        return (start, start.AddDays(1));
    }

    /// <summary>A time with no offset is taken as Riyadh time; any other is converted to UTC.</summary>
    /// <param name="value">The time as the app sent it.</param>
    /// <returns>The same instant with Kind Utc.</returns>
    public static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => TimeZoneInfo.ConvertTimeToUtc(value, Zone),
    };

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
