using MeterSync.Domain;

namespace MeterSync.Tests;

public sealed class GlucoseValueTests
{
    [Theory]
    [InlineData("112", "mg/dL", 112.0, AlertKind.None)]
    [InlineData("69", "mg/dL", 69.0, AlertKind.Low)]
    [InlineData("70", "mg/dL", 70.0, AlertKind.None)]
    [InlineData("251", "mg/dL", 251.0, AlertKind.High)]
    [InlineData("3.5", "mmol/L", 63.0, AlertKind.Low)]
    [InlineData("6.2", "mmol/L", 111.6, AlertKind.None)]
    [InlineData("15", "mmol/L", 270.0, AlertKind.High)]
    public void TryParse_Number_ConvertsToMgDlAndClassifies(string raw, string unit, double expectedMgDl, AlertKind expectedAlert)
    {
        var value = GlucoseValue.TryParse(raw, unit);

        value.Should().BeEquivalentTo(new { MgDl = (decimal?)expectedMgDl, Alert = expectedAlert });
    }

    [Theory]
    [InlineData("HI", AlertKind.High)]
    [InlineData("LO", AlertKind.Low)]
    public void TryParse_OutOfMeterRange_AlertsWithoutValue(string raw, AlertKind expected)
    {
        var value = GlucoseValue.TryParse(raw, "mg/dL");

        value.Should().BeEquivalentTo(new { Raw = raw, MgDl = (decimal?)null, Alert = expected });
    }

    [Theory]
    [InlineData("5,5", "mmol/L")]
    [InlineData("1,200", "mg/dL")]
    [InlineData("abc", "mg/dL")]
    [InlineData("-5", "mg/dL")]
    [InlineData("0", "mg/dL")]
    [InlineData("112", "mg/L")]
    [InlineData(null, "mg/dL")]
    [InlineData("112", null)]
    public void TryParse_Unrecognised_ReturnsNull(string? raw, string? unit) =>
        GlucoseValue.TryParse(raw, unit).Should().BeNull();
}
