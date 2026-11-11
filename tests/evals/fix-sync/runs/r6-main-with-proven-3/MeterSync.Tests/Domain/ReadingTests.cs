using MeterSync.Domain;

namespace MeterSync.Tests.Domain;

public sealed class ReadingTests
{
    private static readonly DateTimeOffset TakenAt = new(2026, 11, 2, 7, 58, 0, TimeSpan.Zero);

    private static Reading? Parse(string raw, string unit = "mg/dL", string vendorId = "r-1") =>
        Reading.TryFromMeter(patientId: 7, meterId: 3, vendorId, raw, unit, TakenAt);

    [Fact]
    public void TryFromMeter_RawHi_StoresSixHundredAndAlertsHigh()
    {
        var reading = Parse("HI");

        reading.Should().NotBeNull();
        reading!.ValueMgDl.Should().Be(600m);
        reading.Alert.Should().Be(AlertKind.High);
        reading.Raw.Should().Be("HI");
    }

    [Theory]
    [InlineData("LO")]
    [InlineData("lo")]
    [InlineData("Lo")]
    public void TryFromMeter_RawLoAnyCase_StoresTwentyAndAlertsLow(string raw)
    {
        var reading = Parse(raw);

        reading.Should().NotBeNull();
        reading!.ValueMgDl.Should().Be(20m);
        reading.Alert.Should().Be(AlertKind.Low);
    }

    [Fact]
    public void TryFromMeter_RawHiInMmol_StillStoresSixHundredMgDl()
    {
        var reading = Parse("hi", "mmol/L");

        reading!.ValueMgDl.Should().Be(600m);
        reading.Alert.Should().Be(AlertKind.High);
    }

    [Fact]
    public void TryFromMeter_MgDlNumber_KeepsValueAndIds()
    {
        var reading = Parse("112");

        reading.Should().NotBeNull();
        reading!.ValueMgDl.Should().Be(112m);
        reading.Alert.Should().Be(AlertKind.None);
        reading.PatientId.Should().Be(7);
        reading.MeterId.Should().Be(3);
        reading.VendorId.Should().Be("r-1");
        reading.TakenAt.Should().Be(TakenAt);
        reading.AlertSentAt.Should().BeNull();
        reading.AlertClaimedAt.Should().BeNull();
    }

    [Theory]
    [InlineData("6.2", "mmol/L", 111.6, AlertKind.None)]
    [InlineData("15", "mmol/L", 270, AlertKind.High)]
    [InlineData("3.1", "mmol/L", 55.8, AlertKind.Low)]
    [InlineData("5.55", "mmol/L", 99.9, AlertKind.None)]
    [InlineData("6.2", "MMOL/L", 111.6, AlertKind.None)]
    [InlineData("112", "MG/DL", 112, AlertKind.None)]
    public void TryFromMeter_UnitsAnyCase_ConvertsToMgDlRoundedToOneDecimal(string raw, string unit, double expected, AlertKind alert)
    {
        var reading = Parse(raw, unit);

        reading.Should().NotBeNull();
        reading!.ValueMgDl.Should().Be((decimal)expected);
        reading.Alert.Should().Be(alert);
    }

    [Theory]
    [InlineData("69.9", AlertKind.Low)]
    [InlineData("70", AlertKind.None)]
    [InlineData("250", AlertKind.None)]
    [InlineData("250.1", AlertKind.High)]
    public void TryFromMeter_AtThresholds_SetsAlert(string raw, AlertKind expected)
    {
        Parse(raw)!.Alert.Should().Be(expected);
    }

    [Theory]
    [InlineData("1,2")]
    [InlineData("1,200")]
    [InlineData("-5")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData(" 112")]
    [InlineData("1e3")]
    [InlineData("100000")]
    [InlineData("12345678901234567")]
    public void TryFromMeter_InvalidRaw_ReturnsNull(string raw)
    {
        Parse(raw).Should().BeNull();
    }

    [Fact]
    public void TryFromMeter_MmolResultOutsideColumnRange_ReturnsNull()
    {
        Parse("6000", "mmol/L").Should().BeNull();
    }

    [Theory]
    [InlineData("mg/dl ")]
    [InlineData("mmol")]
    [InlineData("")]
    public void TryFromMeter_UnknownUnit_ReturnsNull(string unit)
    {
        Parse("112", unit).Should().BeNull();
    }

    [Fact]
    public void TryFromMeter_VendorIdTooLongOrEmpty_ReturnsNull()
    {
        Parse("112", vendorId: new string('x', 65)).Should().BeNull();
        Parse("112", vendorId: "").Should().BeNull();
    }

    [Fact]
    public void MarkAlertSent_ClaimedAlert_SetsSentAtUtcAndClearsClaim()
    {
        var reading = Parse("300")!;
        var sentAt = new DateTimeOffset(2026, 11, 2, 11, 0, 0, TimeSpan.FromHours(3));

        reading.MarkAlertSent(sentAt);

        reading.AlertSentAt.Should().Be(sentAt);
        reading.AlertSentAt!.Value.Offset.Should().Be(TimeSpan.Zero);
        reading.AlertClaimedAt.Should().BeNull();
    }

    [Fact]
    public void MarkAlertSent_NoAlert_Throws()
    {
        var reading = Parse("112")!;

        var act = () => reading.MarkAlertSent(TakenAt);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ReleaseAlertClaim_UnsentAlert_LeavesItUnsentAndUnclaimed()
    {
        var reading = Parse("40")!;

        reading.ReleaseAlertClaim();

        reading.AlertClaimedAt.Should().BeNull();
        reading.AlertSentAt.Should().BeNull();
    }

    [Fact]
    public void TryFromMeter_TakenAtWithOffset_StoresUtc()
    {
        var local = new DateTimeOffset(2026, 11, 2, 10, 58, 0, TimeSpan.FromHours(3));

        var reading = Reading.TryFromMeter(7, 3, "r-1", "112", "mg/dL", local);

        reading!.TakenAt.Offset.Should().Be(TimeSpan.Zero);
        reading.TakenAt.Should().Be(local);
    }
}
