using System.Globalization;
using MeterSync.Application;
using MeterSync.Domain;

namespace MeterSync.Tests;

public sealed class AlertTextTests
{
    private static readonly DateTimeOffset TakenAt = new(2026, 11, 2, 22, 58, 0, TimeSpan.Zero);

    [Fact]
    public void For_Reading_ShowsGregorianDateInRiyadhTime()
    {
        var text = AlertText.For(Make("50"));

        // 22:58 UTC is 01:58 the next day in Riyadh.
        text.Should().Contain("3/11/2026م").And.Contain("01:58").And.NotContain("1448");
    }

    [Fact]
    public void For_Reading_DoesNotDependOnCurrentCulture()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");
            AlertText.For(Make("50")).Should().Contain("3/11/2026م");
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void For_HiReading_SaysAboveMeterRange() =>
        AlertText.For(Make("HI")).Should().Contain("مرتفعة").And.Contain("أعلى من").And.NotContain("HI");

    private static Reading Make(string raw) =>
        Reading.FromMeter(1, 1, "r-1", GlucoseValue.TryParse(raw, "mg/dL")!, TakenAt, TakenAt);
}
