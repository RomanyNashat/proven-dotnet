using MeterSync.Domain;

namespace MeterSync.Tests;

public sealed class ReadingTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FromMeter_RecentOutOfRange_IsPending() =>
        Make("50", Now.AddMinutes(-2)).AlertState.Should().Be(AlertState.Pending);

    [Fact]
    public void FromMeter_OldOutOfRange_IsExpired() =>
        Make("50", Now.AddDays(-30)).AlertState.Should().Be(AlertState.Expired);

    [Fact]
    public void FromMeter_InRange_NeedsNoAlert() =>
        Make("112", Now).AlertState.Should().Be(AlertState.NotNeeded);

    private static Reading Make(string raw, DateTimeOffset takenAt) =>
        Reading.FromMeter(1, 1, "r-1", GlucoseValue.TryParse(raw, "mg/dL")!, takenAt, Now);
}
