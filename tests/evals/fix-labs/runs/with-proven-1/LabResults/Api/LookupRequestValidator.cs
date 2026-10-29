using FluentValidation;
using LabResults.Application;

namespace LabResults.Api;

/// <summary>Only a 10-digit ID reaches the lab API's URL path, so <c>..</c> can't walk to another route.</summary>
public sealed class LookupRequestValidator : AbstractValidator<LookupRequest>
{
    public LookupRequestValidator() =>
        RuleFor(r => r.NationalId)
            .Must(id => NationalId.TryNormalize(id, out _))
            .WithMessage("The national ID must be exactly 10 digits.");
}
