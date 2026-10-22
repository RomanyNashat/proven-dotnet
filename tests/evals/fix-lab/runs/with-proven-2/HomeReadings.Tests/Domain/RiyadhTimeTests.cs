using HomeReadings.Domain;

namespace HomeReadings.Tests.Domain;

public sealed class RiyadhTimeTests
{
    [Fact]
    public void TodayUtcRange_AfterMidnightInRiyadhButBeforeInUtc_IsTheRiyadhDay()
    {
        var now = new DateTimeOffset(2026, 10, 22, 22, 30, 0, TimeSpan.Zero);   // 01:30 on the 23rd in Riyadh

        var (start, end) = RiyadhTime.TodayUtcRange(now);

        start.Should().Be(new DateTime(2026, 10, 22, 21, 0, 0, DateTimeKind.Utc));
        end.Should().Be(new DateTime(2026, 10, 23, 21, 0, 0, DateTimeKind.Utc));
        start.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void TodayUtcRange_EveningInRiyadh_StartsAtThePreviousUtcEvening()
    {
        var now = new DateTimeOffset(2026, 10, 22, 20, 59, 0, TimeSpan.Zero);   // 23:59 on the 22nd in Riyadh

        var (start, _) = RiyadhTime.TodayUtcRange(now);

        start.Should().Be(new DateTime(2026, 10, 21, 21, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void ToUtc_UtcValue_IsUnchanged()
    {
        var utc = new DateTime(2026, 10, 22, 5, 0, 0, DateTimeKind.Utc);

        RiyadhTime.ToUtc(utc).Should().Be(utc);
    }

    [Fact]
    public void ToUtc_NoOffset_IsReadAsRiyadhTime()
    {
        var local = new DateTime(2026, 10, 22, 8, 0, 0, DateTimeKind.Unspecified);

        var utc = RiyadhTime.ToUtc(local);

        utc.Should().Be(new DateTime(2026, 10, 22, 5, 0, 0, DateTimeKind.Utc));
        utc.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void ToUtc_MachineLocalValue_KeepsTheSameInstant()
    {
        var local = new DateTime(2026, 10, 22, 8, 0, 0, DateTimeKind.Local);

        RiyadhTime.ToUtc(local).Should().Be(local.ToUniversalTime());
    }
}
