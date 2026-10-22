namespace HomeReadings.Application;

/// <summary>
/// Patients are in Riyadh (UTC+3, no daylight saving). "Today" and a meter time with no offset mean
/// Riyadh time, not the server's (UTC in the container).
/// </summary>
public static class Riyadh
{
    public static readonly TimeZoneInfo Zone = Find();

    /// <summary>The Riyadh calendar day containing <paramref name="now"/>, as a UTC range [start, end).</summary>
    public static (DateTime StartUtc, DateTime EndUtc) DayOf(DateTimeOffset now)
    {
        var localDate = TimeZoneInfo.ConvertTime(now, Zone).Date;
        return (TimeZoneInfo.ConvertTimeToUtc(localDate, Zone), TimeZoneInfo.ConvertTimeToUtc(localDate.AddDays(1), Zone));
    }

    /// <summary>
    /// PostgreSQL timestamptz needs UTC. A time sent with an offset (or Z) is an exact instant; one sent
    /// without an offset is what the patient's meter showed in Riyadh.
    /// </summary>
    public static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => TimeZoneInfo.ConvertTimeToUtc(value, Zone),
    };

    private static TimeZoneInfo Find()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh");
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            // Slim images can ship without tzdata. Saudi Arabia has kept UTC+3 with no DST since 1947.
            return TimeZoneInfo.CreateCustomTimeZone("Asia/Riyadh", TimeSpan.FromHours(3), "Riyadh", "Riyadh");
        }
    }
}
