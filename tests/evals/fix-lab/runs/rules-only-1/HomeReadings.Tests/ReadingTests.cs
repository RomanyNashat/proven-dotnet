using HomeReadings.Domain;

namespace HomeReadings.Tests;

public sealed class ReadingTests
{
    [Theory]
    [InlineData(11.1, GlucoseUnit.MmolPerLitre, true)]
    [InlineData(11.0, GlucoseUnit.MmolPerLitre, false)]
    [InlineData(200, GlucoseUnit.MgPerDecilitre, true)]
    [InlineData(199, GlucoseUnit.MgPerDecilitre, false)]
    [InlineData(180, GlucoseUnit.MgPerDecilitre, false)]
    [InlineData(25, GlucoseUnit.MgPerDecilitre, false)]
    public void NeedsAlert_ValueInEitherUnit_ComparesInMmolPerLitre(double value, string unit, bool expected)
    {
        var reading = new Reading(1, "1234567890", (decimal)value, unit, DateTime.UnixEpoch, null, DateTime.UnixEpoch);

        reading.NeedsAlert.Should().Be(expected);
    }
}
