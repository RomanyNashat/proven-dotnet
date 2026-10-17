using ClinicReminders.Application;

namespace ClinicReminders.Tests.Application;

public sealed class RiyadhTimeTests
{
    [Fact]
    public void Zone_AnyDate_IsThreeHoursAheadOfUtc()
    {
        RiyadhTime.Zone.GetUtcOffset(new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc)).Should().Be(TimeSpan.FromHours(3));
        RiyadhTime.Zone.GetUtcOffset(new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc)).Should().Be(TimeSpan.FromHours(3));
    }
}
