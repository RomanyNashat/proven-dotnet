using FluentAssertions;
using HomeReadings.Application;
using Xunit;

namespace HomeReadings.Tests;

public sealed class NewReadingValidatorTests
{
    private static readonly DateTime Now = new(2026, 10, 22, 5, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Validate_ArabicDigitsAndRiyadhTime_Normalizes()
    {
        var request = new NewReading("١٠٠٠٠٠٠٠٠١", "٧٫٥", "mmol/l", new DateTime(2026, 10, 22, 7, 30, 0), "  after breakfast ");

        var valid = NewReadingValidator.Validate(request, Now, out var errors);

        errors.Should().BeEmpty();
        valid.Should().Be(new ValidReading("1000000001", 7.5m, "mmol/L", new DateTime(2026, 10, 22, 4, 30, 0, DateTimeKind.Utc), "after breakfast"));
    }

    [Fact]
    public void Validate_BadFields_ReportsEachField()
    {
        var request = new NewReading("12345", "HI", "mmol", Now.AddHours(1), new string('x', 501));

        var valid = NewReadingValidator.Validate(request, Now, out var errors);

        valid.Should().BeNull();
        errors.Keys.Should().BeEquivalentTo("nationalId", "value", "unit", "collectedAt", "notes");
    }

    [Theory]
    [InlineData("61", "mmol/L")]
    [InlineData("1101", "mg/dL")]
    [InlineData("0", "mmol/L")]
    public void Validate_ValueOutsideMeterRange_IsRefused(string value, string unit)
    {
        NewReadingValidator.Validate(new NewReading("2000000002", value, unit, Now, null), Now, out var errors);

        errors.Should().ContainKey("value");
    }
}
