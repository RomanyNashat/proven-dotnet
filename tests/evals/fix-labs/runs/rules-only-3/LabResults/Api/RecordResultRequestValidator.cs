using FluentValidation;
using LabResults.Domain;

namespace LabResults.Api;

public sealed class RecordResultRequestValidator : AbstractValidator<RecordResultRequest>
{
    // numeric(10,3): seven digits before the point, three after.
    private const decimal ValueLimit = 10_000_000m;

    public RecordResultRequestValidator()
    {
        RuleFor(r => r.PatientId).GreaterThan(0);
        RuleFor(r => r.NationalId).NotEmpty().Matches(InputPatterns.NationalId());
        RuleFor(r => r.Phone).NotEmpty().Matches(InputPatterns.Phone());
        RuleFor(r => r.Test).NotEmpty().MaximumLength(LabResult.TestMaxLength);
        RuleFor(r => r.Unit).NotEmpty().MaximumLength(LabResult.UnitMaxLength);
        RuleFor(r => r.Value)
            .ExclusiveBetween(-ValueLimit, ValueLimit)
            .Must(v => decimal.Round(v, 3) == v).WithMessage("'Value' can have at most 3 decimal places.");
        RuleFor(r => r.Notes).MaximumLength(LabResult.NotesMaxLength);
    }
}
