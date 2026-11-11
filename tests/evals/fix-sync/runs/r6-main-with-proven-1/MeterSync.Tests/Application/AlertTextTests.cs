using System.Globalization;
using MeterSync.Application;
using MeterSync.Domain;

namespace MeterSync.Tests.Application;

public sealed class AlertTextTests
{
    private static readonly DateTimeOffset TakenAt = new(2026, 11, 2, 7, 58, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("ar-SA")]
    [InlineData("th-TH")]
    [InlineData("en-US")]
    [InlineData("")]
    public void For_AnyCurrentCulture_ShowsRiyadhTimeInGregorianLatinDigits(string currentCulture)
    {
        var reading = Reading.FromVendor(1, 2, "r-1", "300", "mg/dL", TakenAt)!;
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(currentCulture);

            AlertText.For(reading).Should().Contain("02/11/2026 10:58");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("300", "مرتفعة")]
    [InlineData("50", "منخفضة")]
    public void For_AlertKind_NamesTheLevelAndRawValue(string value, string level)
    {
        var reading = Reading.FromVendor(1, 2, "r-1", value, "mg/dL", TakenAt)!;

        var text = AlertText.For(reading);

        text.Should().Contain(level).And.Contain($"{value} mg/dL");
    }

    [Fact]
    public void ResolveRiyadh_ZoneNotOnMachine_FallsBackToFixedPlusThree()
    {
        var zone = AlertText.ResolveRiyadh(_ => null);

        zone.BaseUtcOffset.Should().Be(TimeSpan.FromHours(3));
        zone.SupportsDaylightSavingTime.Should().BeFalse();
    }

    [Fact]
    public void ResolveRiyadh_ZoneOnMachine_UsesIt()
    {
        var system = TimeZoneInfo.CreateCustomTimeZone("Asia/Riyadh", TimeSpan.FromHours(3), "Riyadh", "Riyadh");

        AlertText.ResolveRiyadh(id => id == "Asia/Riyadh" ? system : null).Should().BeSameAs(system);
    }
}
