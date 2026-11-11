using MeterSync.Domain;

namespace MeterSync.Tests.Domain;

public sealed class ReadingTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("112", "mg/dL", 112.0, AlertKind.None)]
    [InlineData("69.9", "mg/dL", 69.9, AlertKind.Low)]
    [InlineData("70", "mg/dL", 70.0, AlertKind.None)]
    [InlineData("250", "mg/dL", 250.0, AlertKind.None)]
    [InlineData("250.1", "mg/dL", 250.1, AlertKind.High)]
    [InlineData("15", "mmol/L", 270.0, AlertKind.High)]
    [InlineData("3.5", "mmol/L", 63.0, AlertKind.Low)]
    [InlineData("5.5", "mmol/L", 99.0, AlertKind.None)]
    [InlineData("15", "MMOL/L", 270.0, AlertKind.High)]
    [InlineData("HI", "mg/dL", 600.0, AlertKind.High)]
    [InlineData("HI", "mmol/L", 600.0, AlertKind.High)]
    [InlineData("LO", "mg/dL", 20.0, AlertKind.Low)]
    [InlineData("LO", "mmol/L", 20.0, AlertKind.Low)]
    [InlineData("hi", "mg/dL", 600.0, AlertKind.High)]
    public void TryFromMeter_ValidReading_ConvertsToMgDlAndClassifies(string raw, string unit, double expectedMgDl, AlertKind expectedAlert)
    {
        var ok = Reading.TryFromMeter(1, 2, "r-1", raw, unit, Now, out var reading);

        ok.Should().BeTrue();
        reading!.ValueMgDl.Should().Be((decimal)expectedMgDl);
        reading.Alert.Should().Be(expectedAlert);
    }

    [Theory]
    [InlineData("1,234", "mg/dL")]
    [InlineData("-5", "mg/dL")]
    [InlineData("+5", "mg/dL")]
    [InlineData(" 112", "mg/dL")]
    [InlineData("112 ", "mg/dL")]
    [InlineData("1e3", "mg/dL")]
    [InlineData("abc", "mg/dL")]
    [InlineData("", "mg/dL")]
    [InlineData("١١٢", "mg/dL")]
    [InlineData("112", "mg")]
    [InlineData("112", "")]
    [InlineData("112", "mol/L")]
    [InlineData("12345678901234567", "mg/dL")]
    [InlineData("99999", "mmol/L")]
    public void TryFromMeter_UninterpretableValueOrUnit_IsRejected(string raw, string unit)
    {
        var ok = Reading.TryFromMeter(1, 2, "r-1", raw, unit, Now, out var reading);

        ok.Should().BeFalse();
        reading.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void TryFromMeter_MissingVendorId_IsRejected(string? vendorId)
    {
        Reading.TryFromMeter(1, 2, vendorId!, "112", "mg/dL", Now, out _).Should().BeFalse();
    }

    [Fact]
    public void TryFromMeter_VendorIdLongerThan64_IsRejected()
    {
        Reading.TryFromMeter(1, 2, new string('r', 65), "112", "mg/dL", Now, out _).Should().BeFalse();
    }

    [Fact]
    public void TryFromMeter_TakenAtWithOffset_IsStoredAsUtc()
    {
        var riyadhMorning = new DateTimeOffset(2026, 11, 2, 10, 58, 0, TimeSpan.FromHours(3));

        Reading.TryFromMeter(1, 2, "r-1", "112", "mg/dL", riyadhMorning, out var reading);

        reading!.TakenAt.Offset.Should().Be(TimeSpan.Zero);
        reading.TakenAt.Should().Be(riyadhMorning);
    }

    [Fact]
    public void DueForAlert_OutOfRangeRecentUnsent_IsTrue()
    {
        IsDue(Make("300", takenAt: Now.AddHours(-1))).Should().BeTrue();
    }

    [Fact]
    public void DueForAlert_TakenExactlyAtWindowStart_IsTrue()
    {
        IsDue(Make("300", takenAt: Now - Reading.AlertWindow)).Should().BeTrue();
    }

    [Fact]
    public void DueForAlert_TakenBeforeWindow_IsFalse()
    {
        IsDue(Make("300", takenAt: Now - Reading.AlertWindow - TimeSpan.FromSeconds(1))).Should().BeFalse();
    }

    [Fact]
    public void DueForAlert_InRange_IsFalse()
    {
        IsDue(Make("112", takenAt: Now)).Should().BeFalse();
    }

    [Fact]
    public void DueForAlert_AlreadySent_IsFalse()
    {
        var reading = Make("300", takenAt: Now);
        reading.MarkAlertSent(Now);

        IsDue(reading).Should().BeFalse();
    }

    [Fact]
    public void DueForAlert_ClaimLeaseStillRunning_IsFalse()
    {
        var reading = Make("300", takenAt: Now);
        reading.ClaimAlert(Now.AddMinutes(-1));

        IsDue(reading).Should().BeFalse();
    }

    [Fact]
    public void DueForAlert_ClaimLeaseExpired_IsTrue()
    {
        var reading = Make("300", takenAt: Now);
        reading.ClaimAlert(Now - Reading.AlertLease - TimeSpan.FromSeconds(1));

        IsDue(reading).Should().BeTrue();
    }

    [Fact]
    public void DueForAlert_AttemptsExhausted_IsFalse()
    {
        var reading = Make("300", takenAt: Now);
        for (var i = 0; i < Reading.MaxAlertAttempts; i++)
        {
            reading.ClaimAlert(Now.AddHours(-2));
        }

        IsDue(reading).Should().BeFalse();
    }

    [Fact]
    public void ClaimableForAlert_UnsentWithoutLease_IsTrue()
    {
        IsClaimable(Make("300", takenAt: Now)).Should().BeTrue();
    }

    [Fact]
    public void ClaimableForAlert_AnotherPodHoldsLease_IsFalse()
    {
        var reading = Make("300", takenAt: Now);
        reading.ClaimAlert(Now);

        IsClaimable(reading).Should().BeFalse();
    }

    [Fact]
    public void ClaimableForAlert_LeaseExpired_IsTrue()
    {
        var reading = Make("300", takenAt: Now);
        reading.ClaimAlert(Now - Reading.AlertLease - TimeSpan.FromSeconds(1));

        IsClaimable(reading).Should().BeTrue();
    }

    [Fact]
    public void ClaimableForAlert_AlreadySent_IsFalse()
    {
        var reading = Make("300", takenAt: Now);
        reading.MarkAlertSent(Now);

        IsClaimable(reading).Should().BeFalse();
    }

    [Fact]
    public void ClaimableForAlert_AttemptsExhausted_IsFalse()
    {
        var reading = Make("300", takenAt: Now);
        for (var i = 0; i < Reading.MaxAlertAttempts; i++)
        {
            reading.ClaimAlert(Now.AddHours(-2));
        }

        IsClaimable(reading).Should().BeFalse();
    }

    [Fact]
    public void ClaimAlert_SetsLeaseAndCountsAttempt()
    {
        var reading = Make("300", takenAt: Now);

        reading.ClaimAlert(Now);

        reading.AlertClaimedUntil.Should().Be(Now + Reading.AlertLease);
        reading.AlertAttempts.Should().Be(1);
    }

    private static bool IsDue(Reading reading) => Reading.DueForAlert(Now).Compile()(reading);

    private static bool IsClaimable(Reading reading) => Reading.ClaimableForAlert(Now).Compile()(reading);

    private static Reading Make(string raw, DateTimeOffset takenAt)
    {
        Reading.TryFromMeter(1, 2, "r-1", raw, "mg/dL", takenAt, out var reading).Should().BeTrue();
        return reading!;
    }
}
