using MeterSync.Domain;

namespace MeterSync.Tests.Domain;

public sealed class ReadingTests
{
    private static readonly DateTimeOffset LinkedAt = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset AfterLink = new(2026, 11, 2, 7, 58, 0, TimeSpan.Zero);
    private static readonly Meter Meter = new(patientId: 7, serialNumber: "SN-1", linkedAt: LinkedAt);

    [Theory]
    [InlineData("112", 112, AlertKind.None)]
    [InlineData("69.9", 69.9, AlertKind.Low)]
    [InlineData("70", 70, AlertKind.None)]
    [InlineData("250", 250, AlertKind.None)]
    [InlineData("250.1", 250.1, AlertKind.High)]
    public void TryFromMeter_MgDlNumber_StoresValueAndClassifies(string raw, double expected, AlertKind alert)
    {
        var ok = Reading.TryFromMeter(Meter, "r-1", raw, "mg/dL", AfterLink, out var reading, out _);

        ok.Should().BeTrue();
        reading!.ValueMgDl.Should().Be((decimal)expected);
        reading.Alert.Should().Be(alert);
    }

    [Theory]
    [InlineData("3.5", 63, AlertKind.Low)]
    [InlineData("5.5", 99, AlertKind.None)]
    [InlineData("14", 252, AlertKind.High)]
    [InlineData("13.9", 250.2, AlertKind.High)]
    public void TryFromMeter_MmolNumber_ConvertsToMgDlBeforeClassifying(string raw, double expectedMgDl, AlertKind alert)
    {
        var ok = Reading.TryFromMeter(Meter, "r-1", raw, "mmol/L", AfterLink, out var reading, out _);

        ok.Should().BeTrue();
        reading!.ValueMgDl.Should().Be((decimal)expectedMgDl);
        reading.Alert.Should().Be(alert);
        reading.Unit.Should().Be("mmol/L");
        reading.Raw.Should().Be(raw);
    }

    [Fact]
    public void TryFromMeter_MmolNumber_RoundsToTheColumnScale()
    {
        Reading.TryFromMeter(Meter, "r-1", "5.55", "mmol/L", AfterLink, out var reading, out _);

        reading!.ValueMgDl.Should().Be(99.9m);
    }

    [Theory]
    [InlineData("HI", AlertKind.High)]
    [InlineData("hi", AlertKind.High)]
    [InlineData("LO", AlertKind.Low)]
    [InlineData("Lo", AlertKind.Low)]
    public void TryFromMeter_OutOfMeterRange_AlertsWithNoValue(string raw, AlertKind alert)
    {
        var ok = Reading.TryFromMeter(Meter, "r-1", raw, "mg/dL", AfterLink, out var reading, out _);

        ok.Should().BeTrue();
        reading!.Alert.Should().Be(alert);
        reading.ValueMgDl.Should().BeNull();
        reading.Raw.Should().Be(raw);
    }

    [Theory]
    [InlineData("1,2")]
    [InlineData("1,200")]
    [InlineData("-5")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    [InlineData("100000")]
    public void TryFromMeter_ValueNotAReading_IsRejected(string? raw)
    {
        var ok = Reading.TryFromMeter(Meter, "r-1", raw, "mg/dL", AfterLink, out var reading, out var rejection);

        ok.Should().BeFalse();
        reading.Should().BeNull();
        rejection.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("mmol")]
    [InlineData("%")]
    [InlineData("")]
    [InlineData(null)]
    public void TryFromMeter_UnknownUnit_IsRejected(string? unit)
    {
        var ok = Reading.TryFromMeter(Meter, "r-1", "112", unit, AfterLink, out _, out var rejection);

        ok.Should().BeFalse();
        rejection.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TryFromMeter_RejectionReason_DoesNotContainTheValue()
    {
        Reading.TryFromMeter(Meter, "r-1", "987654", "mg/dL", AfterLink, out _, out var rejection);

        rejection.Should().NotContain("987654");
    }

    [Theory]
    [InlineData(64, true)]
    [InlineData(65, false)]
    [InlineData(0, false)]
    public void TryFromMeter_VendorIdLength_RejectsWhatTheColumnCannotHold(int length, bool accepted)
    {
        var ok = Reading.TryFromMeter(Meter, new string('x', length), "112", "mg/dL", AfterLink, out _, out _);

        ok.Should().Be(accepted);
    }

    [Fact]
    public void TryFromMeter_RawLongerThan16_IsRejected()
    {
        var ok = Reading.TryFromMeter(Meter, "r-1", "1" + new string('0', 16), "mg/dL", AfterLink, out _, out _);

        ok.Should().BeFalse();
    }

    [Fact]
    public void TryFromMeter_UnitLongerThan8_IsRejected()
    {
        var ok = Reading.TryFromMeter(Meter, "r-1", "112", "mg/dL....", AfterLink, out _, out _);

        ok.Should().BeFalse();
    }

    [Fact]
    public void TryFromMeter_TakenBeforeTheMeterWasLinked_StoredWithoutAlert()
    {
        var ok = Reading.TryFromMeter(Meter, "r-1", "HI", "mg/dL", LinkedAt.AddDays(-30), out var reading, out _);

        ok.Should().BeTrue();
        reading!.Alert.Should().Be(AlertKind.None);
    }

    [Fact]
    public void TryFromMeter_TakenExactlyAtLink_Alerts()
    {
        Reading.TryFromMeter(Meter, "r-1", "40", "mg/dL", LinkedAt, out var reading, out _);

        reading!.Alert.Should().Be(AlertKind.Low);
    }

    [Fact]
    public void TryFromMeter_Valid_CopiesMeterAndVendorFieldsAndStoresUtc()
    {
        var takenAt = new DateTimeOffset(2026, 11, 2, 10, 58, 0, TimeSpan.FromHours(3));

        Reading.TryFromMeter(Meter, "r-8812", "112", "mg/dL", takenAt, out var reading, out _);

        reading!.PatientId.Should().Be(7);
        reading.VendorId.Should().Be("r-8812");
        reading.TakenAt.Should().Be(takenAt);
        reading.TakenAt.Offset.Should().Be(TimeSpan.Zero);
        reading.AlertAttempts.Should().Be(0);
        reading.AlertClaimedAt.Should().BeNull();
    }
}
