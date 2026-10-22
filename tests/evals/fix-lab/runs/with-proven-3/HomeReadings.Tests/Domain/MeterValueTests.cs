using HomeReadings.Domain;
using HomeReadings.Tests.Support;

namespace HomeReadings.Tests.Domain;

public sealed class MeterValueTests
{
    [Theory]
    [InlineData("12.5", 12.5)]
    [InlineData(" 6.2 ", 6.2)]
    [InlineData("5,6", 5.6)]
    [InlineData("١٢٫٥", 12.5)]   // ١٢٫٥, Arabic-Indic digits and separator
    [InlineData("۱۸۰", 180)]          // ۱۸۰, Eastern Arabic-Indic digits
    [InlineData("180", 180)]
    public void TryParse_NumberAsTheMeterShowsIt_ReadsTheNumber(string text, double expected)
    {
        MeterValue.TryParse(text, out var value).Should().BeTrue();

        value.Should().Be(new MeterValue((decimal)expected, null));
    }

    [Theory]
    [InlineData("HI", MeterLimit.High)]
    [InlineData("hi", MeterLimit.High)]
    [InlineData("LO", MeterLimit.Low)]
    [InlineData(" Lo ", MeterLimit.Low)]
    public void TryParse_MeterOutOfRange_ReadsTheLimit(string text, MeterLimit expected)
    {
        MeterValue.TryParse(text, out var value).Should().BeTrue();

        value.Should().Be(new MeterValue(null, expected));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-4.0")]
    [InlineData("1,234.5")]
    [InlineData("10000")]
    [InlineData("1e3")]
    public void TryParse_NotAReading_IsRefused(string? text)
    {
        MeterValue.TryParse(text, out _).Should().BeFalse();
    }

    [Fact]
    public void TryParse_UnderACultureWithArabicSeparators_StillReadsADot()
    {
        using var _ = new CultureScope(CultureScope.ArabicSeparators());

        MeterValue.TryParse("12.5", out var value).Should().BeTrue();

        value.Number.Should().Be(12.5m);
    }
}
