using FluentAssertions;
using HomeReadings.Application;
using Xunit;

namespace HomeReadings.Tests;

public sealed class PatientTimeTests
{
    [Fact]
    public void Today_AfterMidnightInRiyadhButBeforeInUtc_StartsAtRiyadhMidnight()
    {
        // 01:30 on 22 October in Riyadh is 22:30 on 21 October in UTC.
        var now = new DateTimeOffset(2026, 10, 21, 22, 30, 0, TimeSpan.Zero);

        var (start, end) = PatientTime.Today(now);

        start.Should().Be(new DateTime(2026, 10, 21, 21, 0, 0, DateTimeKind.Utc));
        end.Should().Be(new DateTime(2026, 10, 22, 21, 0, 0, DateTimeKind.Utc));
        start.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void ToUtc_NoOffset_IsRiyadhWallClock() =>
        PatientTime.ToUtc(new DateTime(2026, 10, 22, 8, 0, 0, DateTimeKind.Unspecified))
            .Should().Be(new DateTime(2026, 10, 22, 5, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void ToUtc_Utc_IsUnchanged() =>
        PatientTime.ToUtc(new DateTime(2026, 10, 22, 8, 0, 0, DateTimeKind.Utc))
            .Should().Be(new DateTime(2026, 10, 22, 8, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void ToUtc_Local_ConvertsToUtc()
    {
        var utc = new DateTime(2026, 10, 22, 8, 0, 0, DateTimeKind.Utc);

        PatientTime.ToUtc(utc.ToLocalTime()).Should().Be(utc);
    }

    [Fact]
    public void ToUtc_SubMicrosecondTicks_TruncatedToPostgresPrecision() =>
        (PatientTime.ToUtc(new DateTime(2026, 10, 22, 8, 0, 0, DateTimeKind.Utc).AddTicks(17)).Ticks % 10)
            .Should().Be(0);
}
