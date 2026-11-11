using MeterSync.Domain;

namespace MeterSync.Tests.Domain;

public sealed class ReadingTests
{
    private static readonly DateTimeOffset TakenAt = new(2026, 11, 2, 7, 58, 0, TimeSpan.Zero);

    private static ReadingParseResult Parse(string raw, string unit = "mg/dL", string vendorId = "r-1") =>
        Reading.FromMeter(patientId: 7, meterId: 3, vendorId, raw, unit, TakenAt);

    [Theory]
    [InlineData("HI")]
    [InlineData("hi")]
    [InlineData("Hi")]
    public void FromMeter_HiValue_IsHighAlertWithoutNumericValue(string raw)
    {
        var result = Parse(raw);

        result.Reading.Should().NotBeNull();
        result.Reading!.Alert.Should().Be(AlertKind.High);
        result.Reading.ValueMgDl.Should().BeNull();
        result.Reading.Raw.Should().Be(raw);
    }

    [Theory]
    [InlineData("LO")]
    [InlineData("lo")]
    public void FromMeter_LoValue_IsLowAlertWithoutNumericValue(string raw)
    {
        var result = Parse(raw);

        result.Reading!.Alert.Should().Be(AlertKind.Low);
        result.Reading.ValueMgDl.Should().BeNull();
    }

    [Theory]
    [InlineData("5.5", 99.0, AlertKind.None)]
    [InlineData("15", 270.0, AlertKind.High)]
    [InlineData("3.8", 68.4, AlertKind.Low)]
    [InlineData("5.55", 99.9, AlertKind.None)]
    [InlineData("6.66", 119.9, AlertKind.None)]
    public void FromMeter_MmolPerLitre_ConvertsToMgDlBeforeClassifying(string raw, double expectedMgDl, AlertKind expected)
    {
        var result = Parse(raw, "mmol/L");

        result.Reading!.ValueMgDl.Should().Be((decimal)expectedMgDl);
        result.Reading.Alert.Should().Be(expected);
        result.Reading.Raw.Should().Be(raw);
        result.Reading.Unit.Should().Be("mmol/L");
    }

    [Theory]
    [InlineData("MMOL/L")]
    [InlineData("mmol/l")]
    public void FromMeter_UnitInOtherCase_IsRecognised(string unit)
    {
        Parse("15", unit).Reading!.ValueMgDl.Should().Be(270m);
    }

    [Theory]
    [InlineData("69.9", AlertKind.Low)]
    [InlineData("70", AlertKind.None)]
    [InlineData("250", AlertKind.None)]
    [InlineData("250.1", AlertKind.High)]
    [InlineData("0", AlertKind.Low)]
    public void FromMeter_MgDlThresholds_ClassifiesAlert(string raw, AlertKind expected)
    {
        Parse(raw).Reading!.Alert.Should().Be(expected);
    }

    [Theory]
    [InlineData("1,5")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("-5")]
    [InlineData("1e3")]
    [InlineData(" 112")]
    public void FromMeter_UnparseableValue_IsRejected(string raw)
    {
        var result = Parse(raw);

        result.Reading.Should().BeNull();
        result.Rejection.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void FromMeter_UnknownUnit_IsRejectedWithoutTheValueInTheReason()
    {
        var result = Parse("123", "mg/L");

        result.Reading.Should().BeNull();
        result.Rejection.Should().NotContain("123");
    }

    [Theory]
    [InlineData(65, 1, 1)]
    [InlineData(1, 17, 1)]
    [InlineData(1, 1, 9)]
    [InlineData(0, 1, 1)]
    public void FromMeter_FieldOverColumnLength_IsRejected(int vendorIdLength, int rawLength, int unitLength)
    {
        var result = Reading.FromMeter(7, 3, new string('v', vendorIdLength), new string('1', rawLength), new string('u', unitLength), TakenAt);

        result.Reading.Should().BeNull();
    }

    [Fact]
    public void FromMeter_ValueTooLargeForColumn_IsRejected()
    {
        Parse("100000").Reading.Should().BeNull();
    }

    [Fact]
    public void FromMeter_NullFields_AreRejected()
    {
        Reading.FromMeter(7, 3, null, null, null, TakenAt).Reading.Should().BeNull();
    }

    [Fact]
    public void FromMeter_TakenAtWithOffset_IsStoredAsUtc()
    {
        var local = new DateTimeOffset(2026, 11, 2, 10, 58, 0, TimeSpan.FromHours(3));

        var reading = Reading.FromMeter(7, 3, "r-1", "112", "mg/dL", local).Reading!;

        reading.TakenAt.Offset.Should().Be(TimeSpan.Zero);
        reading.TakenAt.Should().Be(local);
    }

    [Fact]
    public void AlertWindow_IsTwelveHours()
    {
        Reading.AlertWindow.Should().Be(TimeSpan.FromHours(12));
    }
}
