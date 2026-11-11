using System.Globalization;
using FluentAssertions;
using MeterSync.Application;
using MeterSync.Domain;
using Xunit;

namespace MeterSync.Tests;

public sealed class AlertTextTests
{
    private static Reading At(string raw, string unit, DateTimeOffset takenAt)
    {
        Reading.TryFromMeter(1, 2, "r-1", raw, unit, takenAt, out var reading).Should().BeTrue();
        return reading!;
    }

    [Fact]
    public void For_ReadingInUtc_ShowsGregorianDateInRiyadhTime()
    {
        // 22:30 UTC on 2 Nov is 01:30 on 3 Nov in Riyadh (UTC+3).
        var text = AlertText.For(At("45", "mg/dL", new DateTimeOffset(2026, 11, 2, 22, 30, 0, TimeSpan.Zero)));

        text.Should().Contain("03/11/2026").And.Contain("01:30").And.Contain("ميلادي");
    }

    [Theory]
    [InlineData("ar-SA")]
    [InlineData("en-US")]
    [InlineData("")]
    public void For_AnyThreadCulture_ProducesSameText(string culture)
    {
        var reading = At("45", "mg/dL", new DateTimeOffset(2026, 11, 2, 7, 58, 0, TimeSpan.Zero));
        var expected = AlertText.For(reading);
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            AlertText.For(reading).Should().Be(expected);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void For_HiReading_SaysAboveMeterRange()
    {
        var text = AlertText.For(At("HI", "mg/dL", DateTimeOffset.UnixEpoch));

        text.Should().Contain("600").And.Contain("مرتفعة");
    }

    [Fact]
    public void For_LowReading_SaysLow() =>
        AlertText.For(At("45", "mg/dL", DateTimeOffset.UnixEpoch)).Should().Contain("منخفضة").And.Contain("45");
}
