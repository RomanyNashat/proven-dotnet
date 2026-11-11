using MeterSync.Domain;

namespace MeterSync.Tests.Domain;

public sealed class ReadingTests
{
    private static readonly DateTimeOffset TakenAt = new(2026, 11, 2, 7, 58, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("112", "mg/dL", 112.0, AlertKind.None)]
    [InlineData("69", "mg/dL", 69.0, AlertKind.Low)]
    [InlineData("70", "mg/dL", 70.0, AlertKind.None)]
    [InlineData("250", "mg/dL", 250.0, AlertKind.None)]
    [InlineData("251", "mg/dL", 251.0, AlertKind.High)]
    [InlineData("112", "MG/DL", 112.0, AlertKind.None)]
    [InlineData("6.2", "mmol/L", 111.6, AlertKind.None)]
    [InlineData("3.5", "mmol/L", 63.0, AlertKind.Low)]
    [InlineData("15", "MMOL/l", 270.0, AlertKind.High)]
    [InlineData("5.55", "mmol/L", 99.9, AlertKind.None)]
    public void FromVendor_NumericValue_ConvertsToMgDlAndClassifies(string value, string unit, double expectedMgDl, AlertKind expectedAlert)
    {
        var reading = Reading.FromVendor(1, 2, "r-1", value, unit, TakenAt);

        reading.Should().NotBeNull();
        reading!.ValueMgDl.Should().Be((decimal)expectedMgDl);
        reading.Alert.Should().Be(expectedAlert);
        reading.Raw.Should().Be(value);
        reading.Unit.Should().Be(unit);
    }

    [Theory]
    [InlineData("HI", "mg/dL", 600.0, AlertKind.High)]
    [InlineData("HI", "mmol/L", 600.0, AlertKind.High)]
    [InlineData("LO", "mg/dL", 20.0, AlertKind.Low)]
    [InlineData("LO", "mmol/L", 20.0, AlertKind.Low)]
    public void FromVendor_OutOfMeterRange_UsesRangeLimitAndKeepsRaw(string value, string unit, double expectedMgDl, AlertKind expectedAlert)
    {
        var reading = Reading.FromVendor(1, 2, "r-1", value, unit, TakenAt);

        reading.Should().NotBeNull();
        reading!.ValueMgDl.Should().Be((decimal)expectedMgDl);
        reading.Alert.Should().Be(expectedAlert);
        reading.Raw.Should().Be(value);
    }

    [Theory]
    [InlineData("abc", "mg/dL")]
    [InlineData("", "mg/dL")]
    [InlineData(null, "mg/dL")]
    [InlineData("-5", "mg/dL")]
    [InlineData("1,2", "mmol/L")]
    [InlineData("1e3", "mg/dL")]
    [InlineData(" 112", "mg/dL")]
    [InlineData("112", "mmol")]
    [InlineData("112", "")]
    [InlineData("112", null)]
    [InlineData("100000", "mg/dL")]
    [InlineData("12345678901234567", "mg/dL")]
    [InlineData("112", "mg/dL-long")]
    public void FromVendor_UnusableValueOrUnit_ReturnsNull(string? value, string? unit)
    {
        Reading.FromVendor(1, 2, "r-1", value, unit, TakenAt).Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("r-0123456789012345678901234567890123456789012345678901234567890123")]
    public void FromVendor_MissingOrOverlongVendorId_ReturnsNull(string? vendorId)
    {
        Reading.FromVendor(1, 2, vendorId, "112", "mg/dL", TakenAt).Should().BeNull();
    }

    [Fact]
    public void FromVendor_TakenAtWithOffset_StoresUtc()
    {
        var local = new DateTimeOffset(2026, 11, 2, 10, 58, 0, TimeSpan.FromHours(3));

        var reading = Reading.FromVendor(1, 2, "r-1", "112", "mg/dL", local);

        reading!.TakenAt.Offset.Should().Be(TimeSpan.Zero);
        reading.TakenAt.Should().Be(TakenAt);
    }

    [Fact]
    public void FromVendor_Valid_CarriesIdsAndHasNoAlertSentOrFailures()
    {
        var reading = Reading.FromVendor(7, 9, "r-8812", "300", "mg/dL", TakenAt);

        reading!.PatientId.Should().Be(7);
        reading.MeterId.Should().Be(9);
        reading.VendorId.Should().Be("r-8812");
        reading.AlertSentAt.Should().BeNull();
        reading.AlertFailures.Should().Be(0);
    }
}
