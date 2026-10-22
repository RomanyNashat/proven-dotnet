using FluentAssertions;
using HomeReadings.Application;
using Xunit;

namespace HomeReadings.Tests;

public sealed class MeterTextTests
{
    [Theory]
    [InlineData("7.5", 7.5)]
    [InlineData(" 7.5 ", 7.5)]
    [InlineData("7,5", 7.5)]
    [InlineData("٧٫٥", 7.5)]
    [InlineData("۷.۵", 7.5)]
    [InlineData("180", 180)]
    [InlineData("11.10", 11.1)]
    public void TryParseValue_TextAsMeterShowsIt_ParsesInvariantly(string text, double expected)
    {
        MeterText.TryParseValue(text, out var value).Should().BeTrue();
        value.Should().Be((decimal)expected);
    }

    [Theory]
    [InlineData("HI")]
    [InlineData("LO")]
    [InlineData("")]
    [InlineData("-5")]
    [InlineData("1,200.5")]
    [InlineData("7.555")]
    [InlineData("1e3")]
    public void TryParseValue_NotAMeterValue_ReturnsFalse(string text) =>
        MeterText.TryParseValue(text, out _).Should().BeFalse();

    [Fact]
    public void TryParseValue_UnderArabicCulture_StillParsesDecimalPoint()
    {
        var before = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("ar-SA");
            MeterText.TryParseValue("7.5", out var value).Should().BeTrue();
            value.Should().Be(7.5m);
        }
        catch (System.Globalization.CultureNotFoundException)
        {
            // Globalization-invariant machine: there is no ar-SA to switch to, and nothing to prove.
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = before;
        }
    }
}
