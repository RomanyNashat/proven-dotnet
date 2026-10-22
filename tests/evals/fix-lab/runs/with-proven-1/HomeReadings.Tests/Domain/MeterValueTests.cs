using HomeReadings.Domain;

namespace HomeReadings.Tests.Domain;

public sealed class MeterValueTests
{
    [Theory]
    [InlineData("ar-SA")]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    public void TryParse_DecimalPointUnderAnyCulture_ReadsTheSameValue(string culture)
    {
        using var _ = new CultureScope(culture);

        MeterValue.TryParse("12.5", GlucoseUnit.MmolPerLitre, out var reading).Should().BeTrue();

        reading.Value.Should().Be(12.5m);
        reading.OutOfRange.Should().BeNull();
    }

    [Theory]
    [InlineData("١٢٫٥", 12.5)]
    [InlineData(" 7.80 ", 7.8)]
    [InlineData("۱۲.۵", 12.5)]
    public void TryParse_ArabicDigitsOrPadding_ReadsValue(string text, decimal expected)
    {
        MeterValue.TryParse(text, GlucoseUnit.MmolPerLitre, out var reading).Should().BeTrue();

        reading.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("12,5")]
    [InlineData("1,250")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("1e3")]
    [InlineData("10000")]
    public void TryParse_AmbiguousOrImpossible_Fails(string? text)
    {
        MeterValue.TryParse(text, GlucoseUnit.MmolPerLitre, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("HI", GlucoseUnit.MmolPerLitre, 33.3, "HI")]
    [InlineData("hi", GlucoseUnit.MgPerDecilitre, 600, "HI")]
    [InlineData("LO", GlucoseUnit.MmolPerLitre, 1.1, "LO")]
    [InlineData("Lo", GlucoseUnit.MgPerDecilitre, 20, "LO")]
    public void TryParse_MeterOutOfRange_ReadsTheMeterLimit(string text, string unit, decimal expected, string flag)
    {
        MeterValue.TryParse(text, unit, out var reading).Should().BeTrue();

        reading.Value.Should().Be(expected);
        reading.OutOfRange.Should().Be(flag);
    }
}
