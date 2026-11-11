using System.Globalization;
using MeterSync.Application;
using MeterSync.Domain;

namespace MeterSync.Tests;

public sealed class AlertTextTests
{
    [Fact]
    public void For_HighReading_ShowsRiyadhTimeGregorianWithLatinDigits()
    {
        var reading = Reading.TryFromMeter(1, 1, "r-1", "300", "mg/dL", new DateTimeOffset(2026, 11, 2, 21, 30, 0, TimeSpan.Zero), true)!;

        var text = AlertText.For(reading);

        text.Should().Contain("2026/11/03").And.Contain("00:30").And.Contain("ميلادي").And.Contain("300 mg/dL").And.Contain("مرتفعة");
    }

    [Fact]
    public void For_AnyThreadCulture_DoesNotChangeText()
    {
        var reading = Reading.TryFromMeter(1, 1, "r-1", "3.2", "mmol/L", new DateTimeOffset(2026, 11, 2, 7, 58, 0, TimeSpan.Zero), true)!;
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");
            var underArabic = AlertText.For(reading);
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            underArabic.Should().Be(AlertText.For(reading)).And.Contain("2026/11/02").And.Contain("10:58").And.Contain("منخفضة");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData("HI", "أعلى من 600 mg/dL")]
    [InlineData("LO", "أقل من 20 mg/dL")]
    public void For_BeyondMeterRange_SaysSo(string raw, string expected) =>
        AlertText.For(Reading.TryFromMeter(1, 1, "r-1", raw, "mg/dL", DateTimeOffset.UnixEpoch, true)!).Should().Contain(expected);
}
