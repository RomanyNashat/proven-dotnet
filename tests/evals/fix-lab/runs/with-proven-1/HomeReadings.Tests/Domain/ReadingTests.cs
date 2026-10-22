using HomeReadings.Domain;

namespace HomeReadings.Tests.Domain;

public sealed class ReadingTests
{
    private static readonly DateTime At = new(2026, 10, 22, 5, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(11.0, GlucoseUnit.MmolPerLitre, false)]
    [InlineData(11.1, GlucoseUnit.MmolPerLitre, true)]
    [InlineData(150, GlucoseUnit.MgPerDecilitre, false)]   // was an alert: the threshold assumed mmol/L
    [InlineData(200, GlucoseUnit.MgPerDecilitre, true)]
    public void NeedsAlert_ValueInUnit_ComparesWithThatUnitsThreshold(decimal value, string unit, bool expected)
    {
        var reading = new Reading(1, "1012345678", value, unit, At, null, At);

        reading.NeedsAlert.Should().Be(expected);
    }

    [Fact]
    public void Record_HighReading_AddsItsAlertToBeSavedWithIt()
    {
        var reading = Reading.Record(1, "1012345678", new MeterValue(33.3m, "HI"), GlucoseUnit.MmolPerLitre, At, null, At);

        reading.OutOfRange.Should().Be("HI");
        reading.Alert.Should().NotBeNull();
        reading.Alert!.CreatedAt.Should().Be(At);
    }

    [Fact]
    public void Record_NormalReading_HasNoAlert()
    {
        var reading = Reading.Record(1, "1012345678", new MeterValue(6.2m, null), GlucoseUnit.MmolPerLitre, At, null, At);

        reading.Alert.Should().BeNull();
    }
}
