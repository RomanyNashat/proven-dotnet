using System.Globalization;
using MeterSync.Application;
using MeterSync.Domain;

namespace MeterSync.Tests.Application;

public sealed class AlertTextTests
{
    private static readonly DateTimeOffset TakenAt = new(2026, 11, 2, 7, 58, 0, TimeSpan.Zero);

    private static Reading Reading(string raw, string unit = "mg/dL") =>
        MeterSync.Domain.Reading.TryFromMeter(1, 1, "r-1", raw, unit, TakenAt)!;

    [Fact]
    public void For_Reading_UsesGregorianYearAndMonthName()
    {
        var text = AlertText.For(Reading("300"));

        text.Should().Contain("2026").And.Contain("نوفمبر").And.NotContain("1448");
    }

    [Fact]
    public void For_Reading_ShowsRiyadhTime()
    {
        var text = AlertText.For(Reading("300"));

        text.Should().Contain("10:58").And.Contain("2 نوفمبر 2026");
    }

    [Fact]
    public void For_UnderHijriCurrentCulture_StillGregorian()
    {
        var previous = CultureInfo.CurrentCulture;
        var hijri = (CultureInfo)CultureInfo.GetCultureInfo("ar-SA").Clone();
        hijri.DateTimeFormat.Calendar = new UmAlQuraCalendar();
        try
        {
            CultureInfo.CurrentCulture = hijri;

            AlertText.For(Reading("300")).Should().Contain("2026").And.NotContain("1448");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void For_HiReading_SaysAboveSixHundred()
    {
        var text = AlertText.For(Reading("HI"));

        text.Should().Contain("أعلى من 600 mg/dL").And.NotContain("HI");
    }

    [Fact]
    public void For_LoReading_SaysBelowTwenty()
    {
        var text = AlertText.For(Reading("lo"));

        text.Should().Contain("أقل من 20 mg/dL").And.NotContain("lo ");
    }

    [Fact]
    public void For_NumericReading_ShowsValueAsMeterShowedItInLatinDigits()
    {
        var text = AlertText.For(Reading("3.1", "mmol/L"));

        text.Should().Contain("3.1 mmol/L").And.Contain("منخفضة");
        text.Should().NotMatchRegex("[٠-٩۰-۹]");
    }
}
