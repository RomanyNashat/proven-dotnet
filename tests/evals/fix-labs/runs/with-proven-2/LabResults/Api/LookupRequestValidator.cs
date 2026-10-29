using FluentValidation;
using LabResults.Application;

namespace LabResults.Api;

public sealed class LookupRequestValidator : AbstractValidator<LookupRequest>
{
    public LookupRequestValidator() =>
        RuleFor(r => r.NationalId).Must(NationalIdFormat.IsValid).WithMessage("Must be a 10-digit national ID or iqama number.");
}
