using HomeReadings.Domain;

namespace HomeReadings.Tests.Domain;

public sealed class ReadingTests
{
    private static Reading ReadingOf(decimal value, string unit) =>
        new(42, "1012345678", value, unit, new DateTime(2026, 10, 22, 6, 0, 0, DateTimeKind.Utc), null, new DateTime(2026, 10, 22, 6, 1, 0, DateTimeKind.Utc));

    [Theory]
    [InlineData(11.1, "mmol/L")]
    [InlineData(25.0, "mmol/L")]
    [InlineData(200, "mg/dL")]
    [InlineData(320, "mg/dL")]
    public void NeedsAlert_AtOrAboveTheLevelForItsUnit_IsTrue(decimal value, string unit) =>
        ReadingOf(value, unit).NeedsAlert.Should().BeTrue();

    [Theory]
    [InlineData(11.0, "mmol/L")]
    [InlineData(5.4, "mmol/L")]
    [InlineData(120, "mg/dL")]
    [InlineData(199, "mg/dL")]
    public void NeedsAlert_BelowTheLevelForItsUnit_IsFalse(decimal value, string unit) =>
        ReadingOf(value, unit).NeedsAlert.Should().BeFalse();
}
