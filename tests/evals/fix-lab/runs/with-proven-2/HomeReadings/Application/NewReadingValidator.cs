using FluentValidation;
using HomeReadings.Api;
using HomeReadings.Domain;

namespace HomeReadings.Application;

/// <summary>Everything about a new reading that can be checked before the database: a bad value is a 400, never a 500.</summary>
public sealed class NewReadingValidator : AbstractValidator<NewReading>
{
    /// <summary>numeric(6,2) holds less than 10,000; no meter shows more than a few hundred in either unit.</summary>
    public const decimal MaxValue = 1000m;

    private static readonly TimeSpan AllowedClockSkew = TimeSpan.FromMinutes(5);

    public NewReadingValidator(TimeProvider time, RequestLanguage language)
    {
        RuleFor(r => r.NationalId)
            .Must(id => id is { Length: 10 } && id.All(char.IsAsciiDigit))
            .WithMessage(_ => Messages.InvalidNationalId(language.Value));

        RuleFor(r => r.Value).Custom((value, context) =>
        {
            if (MeterValue.IsOutOfMeterRange(value))
            {
                context.AddFailure(nameof(NewReading.Value), Messages.MeterOutOfRange(language.Value));
            }
            else if (!MeterValue.TryParse(value, out var number) || number <= 0 || number > MaxValue)
            {
                context.AddFailure(nameof(NewReading.Value), Messages.InvalidValue(language.Value));
            }
        });

        RuleFor(r => r.Unit)
            .Must(unit => GlucoseUnits.TryNormalize(unit, out _))
            .WithMessage(_ => Messages.InvalidUnit(language.Value));

        RuleFor(r => r.Notes)
            .MaximumLength(Reading.NotesMaxLength)
            .WithMessage(_ => Messages.NotesTooLong(language.Value));

        RuleFor(r => r.CollectedAt)
            .Must(at => at != default && RiyadhTime.ToUtc(at) <= time.GetUtcNow().UtcDateTime + AllowedClockSkew)
            .WithMessage(_ => Messages.InvalidCollectedAt(language.Value));
    }
}
