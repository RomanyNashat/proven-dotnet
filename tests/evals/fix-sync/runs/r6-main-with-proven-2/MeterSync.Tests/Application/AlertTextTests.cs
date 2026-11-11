using System.Globalization;
using MeterSync.Application;
using MeterSync.Domain;

namespace MeterSync.Tests.Application;

public sealed class AlertTextTests
{
    private static readonly DateTimeOffset TakenAt = new(2026, 11, 2, 7, 58, 0, TimeSpan.Zero);

    [Fact]
    public void For_ArabicSaudiCurrentCulture_UsesGregorianDateWithLatinDigits()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("ar-SA");
        try
        {
            var text = AlertText.For(AlertKind.High, "300", "mg/dL", TakenAt);

            text.Should().Contain("02/11/2026");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void For_UtcTime_ShowsRiyadhTimeAndSaysSo()
    {
        var text = AlertText.For(AlertKind.Low, "60", "mg/dL", TakenAt);

        text.Should().Contain("10:58").And.Contain("بتوقيت الرياض");
    }

    [Fact]
    public void For_HiReading_SaysAboveSixHundredInArabic()
    {
        var text = AlertText.For(AlertKind.High, "HI", "mg/dL", TakenAt);

        text.Should().Contain("أعلى من 600 ملغ/دل").And.NotContain("HI");
    }

    [Fact]
    public void For_LoReading_SaysBelowTwentyInArabic()
    {
        var text = AlertText.For(AlertKind.Low, "lo", "mmol/L", TakenAt);

        text.Should().Contain("أقل من 20 ملغ/دل").And.NotContain("lo ");
    }

    [Theory]
    [InlineData(AlertKind.Low, "منخفضة")]
    [InlineData(AlertKind.High, "مرتفعة")]
    public void For_NumericReading_ShowsValueUnitAndLevel(AlertKind kind, string level)
    {
        var text = AlertText.For(kind, "15", "mmol/L", TakenAt);

        text.Should().Contain("15").And.Contain("مليمول/لتر").And.Contain(level);
    }

    [Fact]
    public void For_Reading_UsesTheReadingsData()
    {
        var reading = Reading.FromMeter(1, 1, "r-1", "HI", "mg/dL", TakenAt).Reading!;

        AlertText.For(reading).Should().Be(AlertText.For(AlertKind.High, "HI", "mg/dL", TakenAt));
    }
}
