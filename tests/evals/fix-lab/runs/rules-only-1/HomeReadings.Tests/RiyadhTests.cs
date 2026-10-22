using HomeReadings.Application;

namespace HomeReadings.Tests;

public sealed class RiyadhTests
{
    [Fact]
    public void DayOf_AfterMidnightInRiyadhButBeforeInUtc_ReturnsRiyadhDay()
    {
        // 22:30 UTC on the 21st is 01:30 on the 22nd in Riyadh.
        var (start, end) = Riyadh.DayOf(new DateTimeOffset(2026, 10, 21, 22, 30, 0, TimeSpan.Zero));

        start.Should().Be(new DateTime(2026, 10, 21, 21, 0, 0, DateTimeKind.Utc));
        end.Should().Be(new DateTime(2026, 10, 22, 21, 0, 0, DateTimeKind.Utc));
        start.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void ToUtc_NoOffset_IsReadAsRiyadhTime()
    {
        var utc = Riyadh.ToUtc(new DateTime(2026, 10, 22, 8, 0, 0, DateTimeKind.Unspecified));

        utc.Should().Be(new DateTime(2026, 10, 22, 5, 0, 0, DateTimeKind.Utc));
        utc.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void ToUtc_Utc_IsUnchanged()
    {
        var value = new DateTime(2026, 10, 22, 5, 0, 0, DateTimeKind.Utc);

        Riyadh.ToUtc(value).Should().Be(value);
    }

    [Fact]
    public void ToUtc_Local_IsTheSameInstant()
    {
        var instant = new DateTime(2026, 10, 22, 5, 0, 0, DateTimeKind.Utc);

        Riyadh.ToUtc(instant.ToLocalTime()).Should().Be(instant);
    }
}
