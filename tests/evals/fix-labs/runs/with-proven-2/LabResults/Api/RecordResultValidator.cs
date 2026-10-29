using FluentValidation;
using LabResults.Application;

namespace LabResults.Api;

/// <summary>Limits match the columns, so bad input is a 400 and never a database error.</summary>
public sealed class RecordResultValidator : AbstractValidator<RecordResultRequest>
{
    // numeric(10,3): seven digits before the point, three after.
    private const decimal MaxValue = 9_999_999.999m;

    public RecordResultValidator()
    {
        RuleFor(r => r.PatientId).GreaterThan(0);
        RuleFor(r => r.NationalId).Must(NationalIdFormat.IsValid).WithMessage("Must be a 10-digit national ID or iqama number.");

        // Saudi mobile in E.164, the gateway's format (LABS.md). Anything else would let a caller text any
        // number in the world at the service's expense.
        RuleFor(r => r.Phone).Cascade(CascadeMode.Stop).NotEmpty().Matches("^\\+9665[0-9]{8}$").WithMessage("Must be a Saudi mobile number in the form +9665XXXXXXXX.");
        RuleFor(r => r.Test).NotEmpty().MaximumLength(40);
        RuleFor(r => r.Unit).NotEmpty().MaximumLength(16);
        RuleFor(r => r.Value).InclusiveBetween(-MaxValue, MaxValue).PrecisionScale(10, 3, ignoreTrailingZeros: true);
        RuleFor(r => r.Notes).MaximumLength(1000);
    }
}
