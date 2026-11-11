using System.Globalization;
using FluentAssertions;
using MeterSync.Application;
using MeterSync.Domain;

namespace MeterSync.Tests;

public sealed class AlertTextTests
{
    private static Reading At(string raw, string unit, decimal mgDl) =>
        Reading.FromMeter(1, 1, "r-1", raw, unit, mgDl, new DateTimeOffset(2026, 11, 2, 7, 58, 0, TimeSpan.Zero));

    [Fact]
    public void For_ArabicCurrentCulture_ShowsGregorianRiyadhTime()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("ar-SA");
        try
        {
            var text = AlertText.For(At("300", "mg/dL", 300));

            text.Should().Contain("02/11/2026 10:58");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void For_HiReading_SaysAboveMeterRange()
    {
        var text = AlertText.For(At("HI", "mg/dL", 600));

        text.Should().Contain("600").And.Contain("مرتفعة").And.NotContain("HI mg/dL");
    }

    [Fact]
    public void For_LoReading_SaysBelowMeterRange()
    {
        var text = AlertText.For(At("LO", "mg/dL", 20));

        text.Should().Contain("20").And.Contain("منخفضة").And.NotContain("LO mg/dL");
    }
}
