using FluentValidation;
using LabResults.Application;
using LabResults.Infrastructure;

namespace LabResults.Api;

/// <summary>Every limit here matches its column, so bad input is a 400 and never a database error.</summary>
public sealed class RecordResultRequestValidator : AbstractValidator<RecordResultRequest>
{
    public RecordResultRequestValidator()
    {
        RuleFor(r => r.PatientId).GreaterThan(0);
        RuleFor(r => r.NationalId)
            .Must(id => NationalId.TryNormalize(id, out _))
            .WithMessage("Must be a 10-digit national ID starting with 1 or 2.");
        RuleFor(r => r.Phone)
            .Must(phone => SaudiMobile.TryNormalize(phone, out _))
            .WithMessage("Must be a Saudi mobile number, such as +9665XXXXXXXX.");
        RuleFor(r => r.Test).NotEmpty().MaximumLength(LabResultConfiguration.TestMaxLength);
        RuleFor(r => r.Unit).NotEmpty().MaximumLength(LabResultConfiguration.UnitMaxLength);
        RuleFor(r => r.Value).PrecisionScale(10, 3, ignoreTrailingZeros: true);   // numeric(10,3)
        RuleFor(r => r.Notes).MaximumLength(LabResultConfiguration.NotesMaxLength);
    }
}
