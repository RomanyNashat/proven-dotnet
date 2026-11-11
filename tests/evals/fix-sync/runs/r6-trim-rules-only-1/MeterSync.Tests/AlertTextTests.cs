using System.Globalization;
using FluentAssertions;
using MeterSync.Application;
using MeterSync.Domain;
using Xunit;

namespace MeterSync.Tests;

public sealed class AlertTextTests
{
    private static Reading ReadingOf(string raw, string unit, DateTimeOffset takenAt) =>
        Reading.FromMeter(1, 1, "r-1", raw, unit, GlucoseLevel.Parse(raw, unit)!, takenAt);

    [Fact]
    public void For_UtcReading_ShowsGregorianDateAndRiyadhTime()
    {
        var reading = ReadingOf("300", "mg/dL", new DateTimeOffset(2026, 11, 2, 22, 30, 0, TimeSpan.Zero));

        var text = AlertText.For(reading);

        // 22:30 UTC is 01:30 the next day in Riyadh (UTC+3), Gregorian year, never Hijri (1448).
        text.Should().Contain("03/11/2026 م").And.Contain("01:30").And.Contain("بتوقيت الرياض").And.NotContain("1448");
    }

    [Fact]
    public void For_AnyCurrentCulture_FormatsTheSame()
    {
        var reading = ReadingOf("40", "mg/dL", new DateTimeOffset(2026, 11, 2, 7, 58, 0, TimeSpan.Zero));
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");
            var underArabic = AlertText.For(reading);
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            underArabic.Should().Be(AlertText.For(reading));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData("HI", "أعلى من 600")]
    [InlineData("LO", "أقل من 20")]
    public void For_OffScaleReading_SaysWhatItMeans(string raw, string expected)
    {
        var text = AlertText.For(ReadingOf(raw, "mg/dL", DateTimeOffset.UnixEpoch));

        text.Should().Contain(expected).And.NotContain(raw);
    }

    [Fact]
    public void For_LowReading_SaysLow()
    {
        AlertText.For(ReadingOf("3.1", "mmol/L", DateTimeOffset.UnixEpoch)).Should().Contain("منخفضة").And.Contain("3.1 mmol/L");
    }
}
