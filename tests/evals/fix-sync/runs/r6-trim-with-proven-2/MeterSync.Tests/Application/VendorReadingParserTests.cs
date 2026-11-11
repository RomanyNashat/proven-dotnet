using MeterSync.Application;
using MeterSync.Domain;
using MeterSync.Infrastructure;

namespace MeterSync.Tests.Application;

public sealed class VendorReadingParserTests
{
    private static readonly DateTimeOffset TakenAt = new(2026, 11, 2, 7, 58, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("112", "mg/dL", 112, AlertKind.None)]
    [InlineData("5.5", "mmol/L", 99, AlertKind.None)]
    [InlineData("15", "mmol/L", 270, AlertKind.High)]
    [InlineData("3", "mmol/L", 54, AlertKind.Low)]
    [InlineData("70", "mg/dL", 70, AlertKind.None)]
    [InlineData("250", "mg/dL", 250, AlertKind.None)]
    [InlineData("112", "MG/DL", 112, AlertKind.None)]
    public void TryParse_NumericValue_ConvertsToMgDlAndClassifies(string value, string unit, double expectedMgDl, AlertKind expectedAlert)
    {
        var ok = VendorReadingParser.TryParse(new VendorReading("r-1", value, unit, TakenAt), out var parsed, out _);

        ok.Should().BeTrue();
        parsed!.ValueMgDl.Should().Be((decimal)expectedMgDl);
        parsed.ToReading(1, 2).Alert.Should().Be(expectedAlert);
    }

    [Theory]
    [InlineData("HI", 600, AlertKind.High, "HI")]
    [InlineData(" hi ", 600, AlertKind.High, "HI")]
    [InlineData("LO", 20, AlertKind.Low, "LO")]
    [InlineData("lo", 20, AlertKind.Low, "LO")]
    public void TryParse_OutOfMeterRange_StoresLimitAndKeepsRaw(string value, double expectedMgDl, AlertKind expectedAlert, string expectedRaw)
    {
        var ok = VendorReadingParser.TryParse(new VendorReading("r-1", value, "mg/dL", TakenAt), out var parsed, out _);

        ok.Should().BeTrue();
        var reading = parsed!.ToReading(1, 2);
        reading.ValueMgDl.Should().Be((decimal)expectedMgDl);
        reading.Alert.Should().Be(expectedAlert);
        reading.Raw.Should().Be(expectedRaw);
    }

    [Theory]
    [InlineData("r-1", "1,200", "mg/dL")]
    [InlineData("r-1", "112", "mmHg")]
    [InlineData("r-1", "-5", "mg/dL")]
    [InlineData("r-1", "1000", "mg/dL")]
    [InlineData("r-1", "56", "mmol/L")]
    [InlineData("r-1", "abc", "mg/dL")]
    [InlineData("r-1", "", "mg/dL")]
    [InlineData("", "112", "mg/dL")]
    [InlineData("r-1", "11111111111111112", "mg/dL")]
    public void TryParse_InvalidReading_IsRejectedWithReason(string id, string value, string unit)
    {
        var ok = VendorReadingParser.TryParse(new VendorReading(id, value, unit, TakenAt), out var parsed, out var reason);

        ok.Should().BeFalse();
        parsed.Should().BeNull();
        reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TryParse_VendorIdLongerThan64_IsRejected()
    {
        var ok = VendorReadingParser.TryParse(new VendorReading(new string('x', 65), "112", "mg/dL", TakenAt), out _, out _);

        ok.Should().BeFalse();
    }

    [Fact]
    public void TryParse_RejectReason_NeverContainsTheValue()
    {
        VendorReadingParser.TryParse(new VendorReading("r-1", "987654", "mg/dL", TakenAt), out _, out var reason);

        reason.Should().NotContain("987654");
    }

    [Fact]
    public void TryParse_NullFields_IsRejected()
    {
        var ok = VendorReadingParser.TryParse(new VendorReading(null!, null!, null!, TakenAt), out _, out _);

        ok.Should().BeFalse();
    }
}
