using HomeReadings.Domain;

namespace HomeReadings.Tests.Domain;

public sealed class GlucoseUnitsTests
{
    [Theory]
    [InlineData("mmol/L", "mmol/L")]
    [InlineData("MMOL/L", "mmol/L")]
    [InlineData(" mg/dl ", "mg/dL")]
    public void TryNormalize_KnownUnit_ReturnsCanonicalForm(string unit, string expected)
    {
        GlucoseUnits.TryNormalize(unit, out var canonical).Should().BeTrue();

        canonical.Should().Be(expected);
    }

    [Theory]
    [InlineData("stones")]
    [InlineData("")]
    [InlineData(null)]
    public void TryNormalize_UnknownUnit_ReturnsFalse(string? unit) =>
        GlucoseUnits.TryNormalize(unit, out _).Should().BeFalse();

    [Theory]
    [InlineData(250, "mg/dL", 13.9)]
    [InlineData(200, "mg/dL", 11.1)]
    [InlineData(12.4, "mmol/L", 12.4)]
    public void ToMmolPerLitre_ConvertsForTheCareTeam(decimal value, string unit, decimal expected) =>
        GlucoseUnits.ToMmolPerLitre(value, unit).Should().Be(expected);
}
