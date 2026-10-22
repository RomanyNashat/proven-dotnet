using FluentValidation;
using HomeReadings.Domain;

namespace HomeReadings.Application;

/// <summary>Checks a new reading before anything reaches the database. Limits match the columns.</summary>
public sealed class NewReadingValidator : AbstractValidator<NewReading>
{
    public NewReadingValidator()
    {
        RuleFor(r => r.NationalId)
            .Must(id => NationalId.TryNormalize(id, out _))
            .OverridePropertyName("nationalId")
            .WithMessage("nationalId must be a 10-digit national ID or iqama number.");
        RuleFor(r => r.Unit)
            .Must(unit => GlucoseUnit.TryNormalize(unit, out _))
            .OverridePropertyName("unit")
            .WithMessage($"unit must be {GlucoseUnit.MmolPerLitre} or {GlucoseUnit.MgPerDecilitre}.");
        RuleFor(r => r.Value)
            .Must((request, value) => GlucoseUnit.TryNormalize(request.Unit, out var unit) && MeterValue.TryParse(value, unit, out _))
            .When(r => GlucoseUnit.TryNormalize(r.Unit, out _))
            .OverridePropertyName("value")
            .WithMessage("value must be the number the meter shows, with '.' as the decimal point, or HI or LO.");
        RuleFor(r => r.CollectedAt)
            .NotEqual(default(DateTime))
            .OverridePropertyName("collectedAt");
        RuleFor(r => r.Notes)
            .MaximumLength(Reading.NotesMaxLength)
            .OverridePropertyName("notes");
    }
}
