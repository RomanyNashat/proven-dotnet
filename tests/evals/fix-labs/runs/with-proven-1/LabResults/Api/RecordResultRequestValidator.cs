using FluentValidation;
using LabResults.Application;
using LabResults.Domain;

namespace LabResults.Api;

/// <summary>Everything the database or the downstream calls would refuse is refused here as a 400.</summary>
public sealed class RecordResultRequestValidator : AbstractValidator<RecordResultRequest>
{
    // numeric(10,3): 7 digits before the point, 3 after.
    private const int ValuePrecision = 10;
    private const int ValueScale = 3;

    public RecordResultRequestValidator()
    {
        RuleFor(r => r.PatientId).GreaterThan(0);
        RuleFor(r => r.NationalId)
            .Must(id => NationalId.TryNormalize(id, out _))
            .WithMessage("The national ID must be exactly 10 digits.");
        RuleFor(r => r.Phone)
            .Must(phone => SaudiMobile.TryNormalize(phone, out _))
            .WithMessage("The phone must be a Saudi mobile number (+9665XXXXXXXX).");
        RuleFor(r => r.Test).NotEmpty().MaximumLength(LabResult.TestMaxLength);
        RuleFor(r => r.Unit).NotEmpty().MaximumLength(LabResult.UnitMaxLength);
        RuleFor(r => r.Value).PrecisionScale(ValuePrecision, ValueScale, ignoreTrailingZeros: true);
        RuleFor(r => r.Notes).MaximumLength(LabResult.NotesMaxLength);
    }
}
