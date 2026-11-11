using FluentAssertions;
using MeterSync.Domain;
using Xunit;

namespace MeterSync.Tests;

public sealed class GlucoseLevelTests
{
    [Theory]
    [InlineData("HI", "mg/dL", AlertKind.High)]
    [InlineData("LO", "mg/dL", AlertKind.Low)]
    [InlineData("HI", "mmol/L", AlertKind.High)]
    [InlineData("lo", "", AlertKind.Low)]
    public void Parse_OffScaleReading_AlertsWithoutValue(string raw, string unit, AlertKind expected)
    {
        var level = GlucoseLevel.Parse(raw, unit);

        level.Should().Be(new GlucoseLevel(null, expected));
    }

    [Theory]
    [InlineData("112", "mg/dL", 112.0, AlertKind.None)]
    [InlineData("69", "mg/dL", 69.0, AlertKind.Low)]
    [InlineData("251", "mg/dL", 251.0, AlertKind.High)]
    [InlineData("5.6", "mmol/L", 100.8, AlertKind.None)]
    [InlineData("3.5", "mmol/L", 63.0, AlertKind.Low)]
    [InlineData("15", "mmol/L", 270.0, AlertKind.High)]
    public void Parse_Number_ConvertsToMgDlAndClassifies(string raw, string unit, double mgDl, AlertKind expected)
    {
        var level = GlucoseLevel.Parse(raw, unit);

        level.Should().Be(new GlucoseLevel((decimal)mgDl, expected));
    }

    [Theory]
    [InlineData("1,2", "mg/dL")]
    [InlineData("abc", "mg/dL")]
    [InlineData("112", "g/L")]
    [InlineData("112", null)]
    [InlineData("", "mg/dL")]
    [InlineData("1e3", "mg/dL")]
    [InlineData("99999", "mg/dL")]
    [InlineData("0", "mg/dL")]
    public void Parse_Unreadable_ReturnsNull(string raw, string? unit)
    {
        GlucoseLevel.Parse(raw, unit).Should().BeNull();
    }
}
