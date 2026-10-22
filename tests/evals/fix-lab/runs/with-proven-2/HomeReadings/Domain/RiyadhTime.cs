namespace HomeReadings.Domain;

/// <summary>Patients are in Riyadh. Saudi Arabia is UTC+3 all year with no daylight saving, so a fixed offset is exact.</summary>
public static class RiyadhTime
{
    /// <summary>Asia/Riyadh, or a fixed UTC+3 zone on an image without tzdata.</summary>
    public static readonly TimeZoneInfo Zone = Resolve();

    /// <summary>The patient's "today" as a half-open UTC range.</summary>
    /// <param name="now">The current instant.</param>
    /// <returns>Riyadh midnight today and tomorrow, in UTC.</returns>
    public static (DateTime Start, DateTime End) TodayUtcRange(DateTimeOffset now)
    {
        var local = TimeZoneInfo.ConvertTime(now, Zone);
        var start = new DateTimeOffset(local.Date, local.Offset).UtcDateTime;
        return (start, start.AddDays(1));
    }

    /// <summary>
    /// A time the app sent, as UTC. A value with an offset keeps its instant; one with no offset is the
    /// patient's local time, which is Riyadh's.
    /// </summary>
    /// <param name="value">The time as deserialized.</param>
    /// <returns>The same instant in UTC.</returns>
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
