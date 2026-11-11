using MeterSync.Application;
using MeterSync.Domain;

namespace MeterSync.Tests.Application;

public sealed class AlertTextTests
{
    private static readonly DateTimeOffset TakenAt = new(2026, 11, 2, 7, 58, 0, TimeSpan.Zero);
    private static readonly Meter Meter = new(1, "SN-1", TakenAt.AddDays(-1));

    [Fact]
    public void For_HighReading_RendersRiyadhTimeInGregorianWithLatinDigits()
    {
        var text = AlertText.For(ReadingOf("300", "mg/dL"));

        text.Should().Contain("02/11/2026 10:58");
    }

    [Fact]
    public void For_AnyReading_NamesTheGregorianCalendar()
    {
        var text = AlertText.For(ReadingOf("300", "mg/dL"));

        text.Should().Contain("ميلادي");
    }

    [Fact]
    public void For_AnyReading_HasNoArabicIndicDigits()
    {
        var text = AlertText.For(ReadingOf("40", "mg/dL"));

        text.Should().NotMatchRegex("[٠-٩۰-۹]");
    }

    [Theory]
    [InlineData("300", "mg/dL", "300 mg/dL", "مرتفعة")]
    [InlineData("40", "mg/dL", "40 mg/dL", "منخفضة")]
    [InlineData("2.5", "mmol/L", "2.5 mmol/L", "منخفضة")]
    public void For_NumericReading_ShowsTheValueAsTheMeterShowedItAndTheLevel(string raw, string unit, string shown, string level)
    {
        var text = AlertText.For(ReadingOf(raw, unit));

        text.Should().Contain(shown).And.Contain(level);
    }

    [Theory]
    [InlineData("HI", "أعلى من نطاق")]
    [InlineData("LO", "أقل من نطاق")]
    public void For_OutOfMeterRange_SaysSoInsteadOfPrintingTheMarker(string raw, string phrase)
    {
        var text = AlertText.For(ReadingOf(raw, "mg/dL"));

        text.Should().Contain(phrase).And.NotContain($"{raw} mg/dL");
    }

    [Fact]
    public void ResolveRiyadh_ZoneNotOnTheMachine_FallsBackToFixedUtcPlus3()
    {
        var zone = AlertText.ResolveRiyadh(_ => throw new TimeZoneNotFoundException());

        zone.GetUtcOffset(TakenAt).Should().Be(TimeSpan.FromHours(3));
        zone.GetUtcOffset(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero)).Should().Be(TimeSpan.FromHours(3));
    }

    [Fact]
    public void ResolveRiyadh_ZoneFound_UsesIt()
    {
        var found = TimeZoneInfo.CreateCustomTimeZone("Test/Zone", TimeSpan.FromHours(5), "t", "t");

        var zone = AlertText.ResolveRiyadh(_ => found);

        zone.Should().BeSameAs(found);
    }

    private static Reading ReadingOf(string raw, string unit)
    {
        Reading.TryFromMeter(Meter, "r-1", raw, unit, TakenAt, out var reading, out _).Should().BeTrue();
        return reading!;
    }
}
