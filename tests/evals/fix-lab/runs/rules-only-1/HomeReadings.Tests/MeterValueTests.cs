using System.Globalization;
using HomeReadings.Application;

namespace HomeReadings.Tests;

public sealed class MeterValueTests
{
    [Theory]
    [InlineData("ar-SA")]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    public void TryParse_DecimalPointUnderAnyCulture_ParsesSameValue(string culture)
    {
        var before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(culture);
        try
        {
            MeterValue.TryParse("7.8", out var value).Should().BeTrue();
            value.Should().Be(7.8m);
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Theory]
    [InlineData("142", 142)]
    [InlineData(" 5.6 ", 5.6)]
    [InlineData("٧٫٨", 7.8)]
    [InlineData("١٤٢", 142)]
    public void TryParse_MeterText_ReturnsValue(string text, double expected)
    {
        MeterValue.TryParse(text, out var value).Should().BeTrue();
        value.Should().Be((decimal)expected);
    }

    [Theory]
    [InlineData("7,8")]
    [InlineData("1,200")]
    [InlineData("HI")]
    [InlineData("-5")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("1234567890123")]
    public void TryParse_NotAMeterNumber_ReturnsFalse(string? text)
    {
        MeterValue.TryParse(text, out _).Should().BeFalse();
    }
}
