using HomeReadings.Application;

namespace HomeReadings.Tests.Application;

public sealed class RiyadhTimeTests
{
    [Fact]
    public void DayInUtc_JustAfterRiyadhMidnight_IsTheNewRiyadhDay()
    {
        // 22:30 UTC on the 21st is 01:30 on the 22nd in Riyadh.
        var (start, end) = RiyadhTime.DayInUtc(new DateTimeOffset(2026, 10, 21, 22, 30, 0, TimeSpan.Zero));

        start.Should().Be(new DateTime(2026, 10, 21, 21, 0, 0, DateTimeKind.Utc));
        end.Should().Be(new DateTime(2026, 10, 22, 21, 0, 0, DateTimeKind.Utc));
        start.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void DayInUtc_RiyadhAfternoon_StartsAtTheRiyadhMidnightBefore()
    {
        var (start, _) = RiyadhTime.DayInUtc(new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero));

        start.Should().Be(new DateTime(2026, 10, 21, 21, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Zone_OnAnyMachine_IsThreeHoursAhead()
    {
        RiyadhTime.Zone.GetUtcOffset(new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)).Should().Be(TimeSpan.FromHours(3));
    }
}
