using FluentAssertions;
using MeterSync.Domain;
using Xunit;

namespace MeterSync.Tests;

public sealed class ReadingTests
{
    private static readonly DateTimeOffset TakenAt = new(2026, 11, 2, 7, 58, 0, TimeSpan.Zero);

    private static Reading Parse(string raw, string unit)
    {
        Reading.TryFromMeter(1, 2, "r-1", raw, unit, TakenAt, out var reading).Should().BeTrue();
        return reading!;
    }

    [Theory]
    [InlineData("112", "mg/dL", 112, AlertKind.None)]
    [InlineData("65", "mg/dL", 65, AlertKind.Low)]
    [InlineData("251", "mg/dL", 251, AlertKind.High)]
    [InlineData("6.2", "mmol/L", 111.6, AlertKind.None)]
    [InlineData("3.5", "mmol/L", 63, AlertKind.Low)]
    [InlineData("15", "mmol/L", 270, AlertKind.High)]
    public void TryFromMeter_NumericValue_ConvertsToMgDlAndClassifies(string raw, string unit, double mgDl, AlertKind alert)
    {
        var reading = Parse(raw, unit);

        reading.ValueMgDl.Should().Be((decimal)mgDl);
        reading.Alert.Should().Be(alert);
    }

    [Theory]
    [InlineData("HI", AlertKind.High)]
    [InlineData("LO", AlertKind.Low)]
    public void TryFromMeter_OutOfMeterRange_AlertsWithoutNumericValue(string raw, AlertKind alert)
    {
        var reading = Parse(raw, "mg/dL");

        reading.Alert.Should().Be(alert);
        reading.ValueMgDl.Should().BeNull();
        reading.Raw.Should().Be(raw);
    }

    [Theory]
    [InlineData("1,5", "mmol/L")]
    [InlineData("abc", "mg/dL")]
    [InlineData("-5", "mg/dL")]
    [InlineData("", "mg/dL")]
    [InlineData("112", "mg")]
    [InlineData("112", "")]
    public void TryFromMeter_UnreadableValueOrUnit_IsRejected(string raw, string unit) =>
        Reading.TryFromMeter(1, 2, "r-1", raw, unit, TakenAt, out _).Should().BeFalse();
}
