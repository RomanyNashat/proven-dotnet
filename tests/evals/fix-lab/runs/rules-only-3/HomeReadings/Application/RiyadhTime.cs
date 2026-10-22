namespace HomeReadings.Application;

/// <summary>Patients are in Riyadh: "today" and a time sent without an offset mean Riyadh time.</summary>
public static class RiyadhTime
{
    /// <summary>
    /// Saudi Arabia keeps UTC+3 all year, with no daylight saving. A fixed offset is exact and needs no
    /// tzdata, which slim (Alpine) images don't have.
    /// </summary>
    public static readonly TimeSpan Offset = TimeSpan.FromHours(3);

    /// <summary>The start and end (exclusive), in UTC, of the Riyadh calendar day that contains <paramref name="now"/>.</summary>
    public static (DateTime StartUtc, DateTime EndUtc) DayOf(DateTimeOffset now)
    {
        var start = new DateTimeOffset(now.ToOffset(Offset).Date, Offset);
        return (start.UtcDateTime, start.AddDays(1).UtcDateTime);
    }

    /// <summary>
    /// Converts a time from the app to UTC. A time with "Z" or an offset is exact; one without is what the
    /// meter showed in Riyadh. (PostgreSQL's timestamptz takes only UTC from Npgsql: anything else throws.)
    /// </summary>
    public static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value - Offset, DateTimeKind.Utc),
    };
}
