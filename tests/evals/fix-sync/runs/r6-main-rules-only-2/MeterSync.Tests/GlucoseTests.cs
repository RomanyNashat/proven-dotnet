using FluentAssertions;
using MeterSync.Domain;
using Xunit;

namespace MeterSync.Tests;

public sealed class GlucoseTests
{
    [Theory]
    [InlineData("112", "mg/dL", 112.0)]
    [InlineData("5.5", "mmol/L", 99.0)]
    [InlineData("3.5", "mmol/L", 63.0)]
    [InlineData("15", "MMOL/L", 270.0)]
    public void TryParse_Number_ReturnsMgDl(string raw, string unit, double expected)
    {
        Glucose.TryParse(raw, unit, out var glucose).Should().BeTrue();

        glucose.MgDl.Should().Be((decimal)expected);
    }

    [Theory]
    [InlineData("5.5", "mmol/L", AlertKind.None)]
    [InlineData("3.5", "mmol/L", AlertKind.Low)]
    [InlineData("15", "mmol/L", AlertKind.High)]
    [InlineData("69", "mg/dL", AlertKind.Low)]
    [InlineData("70", "mg/dL", AlertKind.None)]
    [InlineData("250", "mg/dL", AlertKind.None)]
    [InlineData("251", "mg/dL", AlertKind.High)]
    [InlineData("HI", "mg/dL", AlertKind.High)]
    [InlineData("LO", "mmol/L", AlertKind.Low)]
    public void Classify_Value_ReturnsAlertKind(string raw, string unit, AlertKind expected)
    {
        Glucose.TryParse(raw, unit, out var glucose).Should().BeTrue();

        glucose.Classify(Reading.LowBelow, Reading.HighAbove).Should().Be(expected);
    }

    [Theory]
    [InlineData("1,200", "mg/dL")]
    [InlineData("5,5", "mmol/L")]
    [InlineData("-5", "mg/dL")]
    [InlineData("0", "mg/dL")]
    [InlineData("abc", "mg/dL")]
    [InlineData("112", "g/L")]
    [InlineData("112", null)]
    [InlineData(null, "mg/dL")]
    public void TryParse_Unrecognised_ReturnsFalse(string? raw, string? unit)
    {
        Glucose.TryParse(raw, unit, out _).Should().BeFalse();
    }
}
