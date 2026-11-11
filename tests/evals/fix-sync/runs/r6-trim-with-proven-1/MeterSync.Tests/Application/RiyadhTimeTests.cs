using MeterSync.Application;

namespace MeterSync.Tests.Application;

public sealed class RiyadhTimeTests
{
    private static readonly DateTimeOffset Utc = new(2026, 11, 2, 7, 58, 0, TimeSpan.Zero);

    [Fact]
    public void Zone_ConvertsUtcToPlusThreeHours()
    {
        TimeZoneInfo.ConvertTime(Utc, RiyadhTime.Zone).DateTime.Should().Be(new DateTime(2026, 11, 2, 10, 58, 0));
    }

    [Fact]
    public void Resolve_ZoneMissingFromMachine_FallsBackToFixedPlusThree()
    {
        var zone = RiyadhTime.Resolve("Nowhere/Missing");

        zone.BaseUtcOffset.Should().Be(TimeSpan.FromHours(3));
        TimeZoneInfo.ConvertTime(Utc, zone).Hour.Should().Be(10);
    }
}
