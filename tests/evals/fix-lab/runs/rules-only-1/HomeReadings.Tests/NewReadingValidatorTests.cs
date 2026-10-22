using HomeReadings.Application;
using HomeReadings.Domain;

namespace HomeReadings.Tests;

public sealed class NewReadingValidatorTests
{
    private static NewReading Valid() =>
        new("1234567890", "7.8", "mmol/L", new DateTime(2026, 10, 22, 8, 0, 0, DateTimeKind.Utc), "after breakfast");

    [Fact]
    public void Validate_ValidRequest_ReturnsReadingInUtc()
    {
        var (reading, errors) = NewReadingValidator.Validate(Valid() with { Unit = "MG/DL", Value = "142" });

        errors.Should().BeEmpty();
        reading!.Unit.Should().Be(GlucoseUnit.MgPerDecilitre);
        reading.Value.Should().Be(142m);
        reading.CollectedAtUtc.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void Validate_NationalIdTooLong_ReturnsError()
    {
        var (_, errors) = NewReadingValidator.Validate(Valid() with { NationalId = "12345678901" });

        errors.Should().ContainKey("nationalId");
    }

    [Fact]
    public void Validate_UnknownUnit_ReturnsError()
    {
        var (_, errors) = NewReadingValidator.Validate(Valid() with { Unit = "mmHg" });

        errors.Should().ContainKey("unit");
    }

    [Fact]
    public void Validate_ValueOutOfRangeForUnit_ReturnsError()
    {
        var (_, errors) = NewReadingValidator.Validate(Valid() with { Value = "142" });

        errors.Should().ContainKey("value");
    }

    [Fact]
    public void Validate_NotesLongerThanColumn_ReturnsError()
    {
        var (_, errors) = NewReadingValidator.Validate(Valid() with { Notes = new string('x', 501) });

        errors.Should().ContainKey("notes");
    }
}
