using MeterSync.Domain;

namespace MeterSync.Tests.Domain;

public sealed class ReadingTests
{
    [Theory]
    [InlineData(69.9, AlertKind.Low)]
    [InlineData(70, AlertKind.None)]
    [InlineData(250, AlertKind.None)]
    [InlineData(250.1, AlertKind.High)]
    public void Classify_ValueAroundThresholds_ReturnsExpectedAlert(double mgDl, AlertKind expected) =>
        Reading.Classify((decimal)mgDl).Should().Be(expected);

    [Fact]
    public void FromMeter_LowValue_SetsLowAlertAndNoClaim()
    {
        var reading = Reading.FromMeter(1, 2, "r-1", "54", "mg/dL", 54m, DateTimeOffset.UnixEpoch);

        reading.Alert.Should().Be(AlertKind.Low);
        reading.AlertAttempts.Should().Be(0);
        reading.AlertClaimedUntil.Should().BeNull();
    }
}
