using FluentAssertions;
using MeterSync.Domain;

namespace MeterSync.Tests;

public sealed class GlucoseTests
{
    [Theory]
    [InlineData("112", "mg/dL", 112)]
    [InlineData("5.5", "mmol/L", 99)]
    [InlineData("15", "mmol/L", 270)]
    [InlineData("HI", "mg/dL", 600)]
    [InlineData("LO", "mmol/L", 20)]
    public void TryToMgDl_ValidReading_ReturnsMgDl(string raw, string unit, decimal expected)
    {
        var ok = Glucose.TryToMgDl(raw, unit, out var mgDl);

        ok.Should().BeTrue();
        mgDl.Should().Be(expected);
    }

    [Theory]
    [InlineData("1,2", "mg/dL")]
    [InlineData("abc", "mg/dL")]
    [InlineData("112", "mmHg")]
    [InlineData("-5", "mg/dL")]
    [InlineData("99999", "mg/dL")]
    public void TryToMgDl_InvalidReading_ReturnsFalse(string raw, string unit)
    {
        Glucose.TryToMgDl(raw, unit, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("HI", "mg/dL", AlertKind.High)]
    [InlineData("LO", "mg/dL", AlertKind.Low)]
    [InlineData("15", "mmol/L", AlertKind.High)]
    [InlineData("5.5", "mmol/L", AlertKind.None)]
    public void FromMeter_ConvertedValue_ClassifiesAlert(string raw, string unit, AlertKind expected)
    {
        Glucose.TryToMgDl(raw, unit, out var mgDl).Should().BeTrue();

        var reading = Reading.FromMeter(1, 1, "r-1", raw, unit, mgDl, DateTimeOffset.UnixEpoch);

        reading.Alert.Should().Be(expected);
    }
}
