using HomeReadings.Domain;

namespace HomeReadings.Tests.Domain;

public sealed class ReadingTests
{
    private static readonly DateTime At = new(2026, 10, 22, 5, 30, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("11.1", "mmol/L", true)]
    [InlineData("11.0", "mmol/L", false)]
    [InlineData("200", "mg/dL", true)]
    [InlineData("180", "mg/dL", false)]
    [InlineData("HI", "mmol/L", true)]
    [InlineData("HI", "mg/dL", true)]
    [InlineData("LO", "mmol/L", false)]
    public void NeedsAlert_ByValueAndUnit_MatchesTheMmolThreshold(string text, string unit, bool expected)
    {
        MeterValue.TryParse(text, out var meter).Should().BeTrue();

        var reading = new Reading(1, "1012345678", meter, unit, At, null, At);

        reading.NeedsAlert.Should().Be(expected);
    }

    [Theory]
    [InlineData("mmol/L", "mmol/L")]
    [InlineData("MMOL/l", "mmol/L")]
    [InlineData("mg/dL", "mg/dL")]
    [InlineData("mg/dl", "mg/dL")]
    public void TryNormalize_KnownUnit_GivesTheCanonicalSpelling(string unit, string expected)
    {
        GlucoseUnit.TryNormalize(unit, out var canonical).Should().BeTrue();

        canonical.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("kg")]
    [InlineData("mmol")]
    public void TryNormalize_UnknownUnit_IsRefused(string? unit)
    {
        GlucoseUnit.TryNormalize(unit, out _).Should().BeFalse();
    }

    [Fact]
    public void ToMmolPerLitre_MgPerDecilitre_Converts()
    {
        GlucoseUnit.ToMmolPerLitre(180m, GlucoseUnit.MgPerDecilitre).Should().Be(10m);
    }
}
