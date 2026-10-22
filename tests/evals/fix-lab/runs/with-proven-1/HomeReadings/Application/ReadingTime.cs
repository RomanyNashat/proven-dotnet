namespace HomeReadings.Application;

/// <summary>Times are stored in UTC; the patient's day is Riyadh's.</summary>
public static class ReadingTime
{
    /// <summary>
    /// The UTC time of a reading. A time with <c>Z</c> stays as it is; one with an offset arrives as Local and
    /// is converted back; one with no offset is the meter's clock, which is Riyadh time.
    /// </summary>
    /// <param name="collectedAt">The time as bound from the request.</param>
    /// <returns>The same instant with Kind=Utc.</returns>
    public static DateTime ToUtc(DateTime collectedAt) => collectedAt.Kind switch
    {
        DateTimeKind.Utc => collectedAt,
        DateTimeKind.Local => collectedAt.ToUniversalTime(),
        _ => TimeZoneInfo.ConvertTimeToUtc(collectedAt, RiyadhTime.Zone),
    };

    /// <summary>The Riyadh calendar day containing <paramref name="now"/>, as a UTC range [start, end).</summary>
    /// <param name="now">The current time.</param>
    /// <returns>Riyadh midnight to the next Riyadh midnight, in UTC.</returns>
    public static (DateTime Start, DateTime End) RiyadhDay(DateTimeOffset now)
    {
        var localMidnight = TimeZoneInfo.ConvertTime(now, RiyadhTime.Zone).Date;
        var start = TimeZoneInfo.ConvertTimeToUtc(localMidnight, RiyadhTime.Zone);
        return (start, start.AddDays(1));
    }
}
