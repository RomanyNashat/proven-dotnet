using FluentValidation;

namespace LabResults.Api;

public sealed class LookupRequestValidator : AbstractValidator<LookupRequest>
{
    public LookupRequestValidator() =>
        RuleFor(r => r.NationalId).NotEmpty().Matches(InputPatterns.NationalId());
}
