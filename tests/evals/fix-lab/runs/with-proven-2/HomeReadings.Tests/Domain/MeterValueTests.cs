using HomeReadings.Domain;

namespace HomeReadings.Tests.Domain;

public sealed class MeterValueTests
{
    [Theory]
    [InlineData("ar-SA")]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    public void TryParse_DotDecimalUnderAnyCulture_ReadsWhatTheMeterShows(string culture)
    {
        using var _ = new CultureScope(culture);

        MeterValue.TryParse("11.5", out var value).Should().BeTrue();

        value.Should().Be(11.5m);
    }

    [Theory]
    [InlineData("١١٫٥", 11.5)]
    [InlineData("۱۱.۵", 11.5)]
    [InlineData("11,5", 11.5)]
    [InlineData(" 6.2 ", 6.2)]
    [InlineData("180", 180)]
    public void TryParse_OtherWaysAMeterOrPhoneWritesIt_ParsesTheSameNumber(string text, decimal expected)
    {
        MeterValue.TryParse(text, out var value).Should().BeTrue();

        value.Should().Be(expected);
    }

    [Theory]
    [InlineData("HI")]
    [InlineData("LO")]
    [InlineData("hi")]
    [InlineData("eleven")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("1.2.3")]
    [InlineData("-5")]
    [InlineData("1,234.5")]
    public void TryParse_NotAPlainNumber_ReturnsFalse(string? text) =>
        MeterValue.TryParse(text, out _).Should().BeFalse();

    [Theory]
    [InlineData("HI", true)]
    [InlineData(" lo ", true)]
    [InlineData("11.5", false)]
    [InlineData(null, false)]
    public void IsOutOfMeterRange_HiOrLo_IsTrue(string? text, bool expected) =>
        MeterValue.IsOutOfMeterRange(text).Should().Be(expected);
}
