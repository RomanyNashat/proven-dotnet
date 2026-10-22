using HomeReadings.Application;

namespace HomeReadings.Tests.Application;

public sealed class ReadingTimeTests
{
    [Fact]
    public void ToUtc_Utc_IsUnchanged()
    {
        var utc = new DateTime(2026, 10, 22, 5, 0, 0, DateTimeKind.Utc);

        ReadingTime.ToUtc(utc).Should().Be(utc).And.BeIn(DateTimeKind.Utc);
    }

    [Fact]
    public void ToUtc_NoOffset_IsReadAsRiyadhTime()
    {
        var meterTime = new DateTime(2026, 10, 22, 8, 0, 0, DateTimeKind.Unspecified);

        ReadingTime.ToUtc(meterTime).Should().Be(new DateTime(2026, 10, 22, 5, 0, 0, DateTimeKind.Utc)).And.BeIn(DateTimeKind.Utc);
    }

    [Fact]
    public void ToUtc_Local_IsConvertedFromTheMachinesZone()
    {
        var local = new DateTimeOffset(2026, 10, 22, 5, 0, 0, TimeSpan.Zero).LocalDateTime;

        ReadingTime.ToUtc(local).Should().Be(new DateTime(2026, 10, 22, 5, 0, 0, DateTimeKind.Utc)).And.BeIn(DateTimeKind.Utc);
    }

    [Theory]
    [InlineData("2026-10-22T22:30:00Z", "2026-10-22T21:00:00Z", "2026-10-23T21:00:00Z")]   // 01:30 on the 23rd in Riyadh
    [InlineData("2026-10-22T20:59:59Z", "2026-10-21T21:00:00Z", "2026-10-22T21:00:00Z")]   // 23:59:59 on the 22nd in Riyadh
    public void RiyadhDay_Now_IsThePatientsLocalDayInUtc(string now, string start, string end)
    {
        var (from, to) = ReadingTime.RiyadhDay(DateTimeOffset.Parse(now, System.Globalization.CultureInfo.InvariantCulture));

        from.Should().Be(DateTime.Parse(start, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal)).And.BeIn(DateTimeKind.Utc);
        to.Should().Be(DateTime.Parse(end, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal)).And.BeIn(DateTimeKind.Utc);
    }
}
