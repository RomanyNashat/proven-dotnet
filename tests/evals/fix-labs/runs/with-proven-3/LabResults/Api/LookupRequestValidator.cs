using FluentValidation;
using LabResults.Application;

namespace LabResults.Api;

/// <summary>The ID goes into the lab's URL path: anything but ten digits could change which path is called.</summary>
public sealed class LookupRequestValidator : AbstractValidator<LookupRequest>
{
    public LookupRequestValidator() =>
        RuleFor(r => r.NationalId)
            .Must(id => NationalId.TryNormalize(id, out _))
            .WithMessage("Must be a 10-digit national ID starting with 1 or 2.");
}
