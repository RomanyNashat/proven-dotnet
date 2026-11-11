using MeterSync.Domain;

namespace MeterSync.Tests;

public sealed class ReadingTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 2, 7, 58, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("112", "mg/dL", 112, AlertKind.None)]
    [InlineData("60", "mg/dL", 60, AlertKind.Low)]
    [InlineData("300", "mg/dL", 300, AlertKind.High)]
    [InlineData("15", "mmol/L", 270, AlertKind.High)]
    [InlineData("3.2", "mmol/L", 57.6, AlertKind.Low)]
    [InlineData("6.1", "mmol/L", 109.8, AlertKind.None)]
    [InlineData("6.1", "MMOL/L", 109.8, AlertKind.None)]
    public void TryFromMeter_Number_ConvertsToMgDlAndClassifies(string raw, string unit, double mgDl, AlertKind alert)
    {
        var reading = Reading.TryFromMeter(1, 1, "r-1", raw, unit, At, worthTexting: true);

        reading.Should().NotBeNull();
        reading!.ValueMgDl.Should().Be((decimal)mgDl);
        reading.Alert.Should().Be(alert);
    }

    [Theory]
    [InlineData("HI", AlertKind.High)]
    [InlineData("LO", AlertKind.Low)]
    public void TryFromMeter_BeyondMeterRange_IsAnAlertWithoutValue(string raw, AlertKind alert)
    {
        var reading = Reading.TryFromMeter(1, 1, "r-1", raw, "mg/dL", At, worthTexting: true);

        reading.Should().NotBeNull();
        reading!.Alert.Should().Be(alert);
        reading.ValueMgDl.Should().BeNull();
        reading.AlertStatus.Should().Be(AlertStatus.Pending);
    }

    [Theory]
    [InlineData("1,2", "mg/dL")]
    [InlineData("-5", "mg/dL")]
    [InlineData("abc", "mg/dL")]
    [InlineData("100", "g/L")]
    [InlineData("9999", "mmol/L")]
    public void TryFromMeter_Unreadable_ReturnsNull(string raw, string unit) =>
        Reading.TryFromMeter(1, 1, "r-1", raw, unit, At, worthTexting: true).Should().BeNull();

    [Fact]
    public void TryFromMeter_OutOfRangeButNotWorthTexting_IsStale() =>
        Reading.TryFromMeter(1, 1, "r-1", "300", "mg/dL", At, worthTexting: false)!.AlertStatus.Should().Be(AlertStatus.Stale);

    [Fact]
    public void TryFromMeter_InRange_NeedsNoAlert() =>
        Reading.TryFromMeter(1, 1, "r-1", "100", "mg/dL", At, worthTexting: true)!.AlertStatus.Should().Be(AlertStatus.NotNeeded);

    [Fact]
    public void TryClaimAlert_AlreadyClaimed_ReturnsFalseUntilLeaseLapses()
    {
        var reading = Reading.TryFromMeter(1, 1, "r-1", "300", "mg/dL", At, worthTexting: true)!;

        reading.TryClaimAlert(At, At.AddMinutes(5)).Should().BeTrue();
        reading.TryClaimAlert(At.AddMinutes(1), At.AddMinutes(6)).Should().BeFalse();
        reading.TryClaimAlert(At.AddMinutes(5), At.AddMinutes(10)).Should().BeTrue();
    }

    [Fact]
    public void AlertFailed_AfterMaxAttempts_GivesUp()
    {
        var reading = Reading.TryFromMeter(1, 1, "r-1", "300", "mg/dL", At, worthTexting: true)!;
        var now = At;
        for (var i = 1; i < Reading.MaxAlertAttempts; i++)
        {
            reading.TryClaimAlert(now, now.AddMinutes(5));
            reading.AlertFailed().Should().BeFalse();
            now = now.AddMinutes(5);
        }

        reading.TryClaimAlert(now, now.AddMinutes(5));
        reading.AlertFailed().Should().BeTrue();
        reading.AlertStatus.Should().Be(AlertStatus.Failed);
    }

    [Fact]
    public void MarkAlertSent_ClaimedAlert_CannotBeClaimedAgain()
    {
        var reading = Reading.TryFromMeter(1, 1, "r-1", "300", "mg/dL", At, worthTexting: true)!;
        reading.TryClaimAlert(At, At.AddMinutes(5));

        reading.MarkAlertSent(At);

        reading.AlertStatus.Should().Be(AlertStatus.Sent);
        reading.TryClaimAlert(At.AddHours(1), At.AddHours(2)).Should().BeFalse();
    }
}
