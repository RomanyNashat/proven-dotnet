using FluentValidation;
using Referrals.Api;

namespace Referrals.Application;

public sealed class CreateReferralValidator : AbstractValidator<CreateReferralRequest>
{
    public CreateReferralValidator()
    {
        RuleFor(r => r.Note).NotEmpty().WithMessage("Every referral needs a note for the clinic.");
        RuleFor(r => r.Reason).MinimumLength(10);
    }
}
