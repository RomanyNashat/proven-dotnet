using FluentAssertions;
using HomeReadings.Domain;
using Xunit;

namespace HomeReadings.Tests;

public sealed class ReadingTests
{
    private static readonly DateTime Now = new(2026, 10, 22, 5, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("11.1", "mmol/L", true)]
    [InlineData("11.0", "mmol/L", false)]
    [InlineData("200", "mg/dL", true)]
    [InlineData("150", "mg/dL", false)]
    [InlineData("150", "MG/DL", false)]
    public void NeedsAlert_ComparesInMmolPerL(string value, string unit, bool expected)
    {
        var reading = new Reading(1, "1000000001", decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture), unit, Now, null, Now);

        reading.NeedsAlert.Should().Be(expected);
    }

    [Fact]
    public void Constructor_UnitInAnyCase_StoresCanonicalUnit() =>
        new Reading(1, "1000000001", 7m, "MMOL/l", Now, null, Now).Unit.Should().Be(GlucoseUnits.MmolPerL);

    [Fact]
    public void Constructor_UnknownUnit_Throws()
    {
        var act = () => new Reading(1, "1000000001", 7m, "mg", Now, null, Now);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
