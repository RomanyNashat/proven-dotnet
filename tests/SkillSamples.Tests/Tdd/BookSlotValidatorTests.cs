using FluentAssertions;
using Xunit;

namespace SkillSamples.Tdd;

public sealed class BookSlotValidatorTests
{
    private readonly BookSlotValidator _validator = new();

    [Fact]
    public async Task Validate_NoPatient_FailsOnPatientId()
    {
        var result = await _validator.ValidateAsync(new BookSlot(PatientId: 0, SlotId: 42));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.PropertyName.Should().Be(nameof(BookSlot.PatientId));
    }

    [Fact]
    public async Task Validate_ValidCommand_IsValid()
    {
        var result = await _validator.ValidateAsync(new BookSlot(7, 42));

        result.IsValid.Should().BeTrue();
    }
}
