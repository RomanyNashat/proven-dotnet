using HomeReadings.Domain;

namespace HomeReadings.Tests.Domain;

public sealed class GlucoseUnitTests
{
    [Theory]
    [InlineData("mmol/L", GlucoseUnit.MmolPerLitre)]
    [InlineData("MMOL/l", GlucoseUnit.MmolPerLitre)]
    [InlineData(" mg/dL ", GlucoseUnit.MgPerDecilitre)]
    public void TryNormalize_KnownUnit_ReturnsCanonicalName(string text, string expected)
    {
        GlucoseUnit.TryNormalize(text, out var unit).Should().BeTrue();

        unit.Should().Be(expected);
    }

    [Theory]
    [InlineData("mg")]
    [InlineData("")]
    [InlineData(null)]
    public void TryNormalize_UnknownUnit_Fails(string? text)
    {
        GlucoseUnit.TryNormalize(text, out _).Should().BeFalse();
    }
}
