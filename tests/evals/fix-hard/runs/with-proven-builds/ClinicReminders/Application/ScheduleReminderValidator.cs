using FluentValidation;

namespace ClinicReminders.Application;

public sealed class ScheduleReminderValidator : AbstractValidator<ScheduleReminder>
{
    public ScheduleReminderValidator(TimeProvider time)
    {
        // ASCII digits with an optional leading '+', within the 20-character column. \d would also accept
        // Arabic-Indic digits, which the provider doesn't take.
        RuleFor(r => r.Mobile)
            .NotEmpty()
            .MaximumLength(20)
            .Matches("^\\+?[0-9]{8,19}\\z").WithMessage("Mobile must be 8 to 19 ASCII digits, optionally after a '+'.");

        RuleFor(r => r.AppointmentAt)
            .Must(at => at > time.GetUtcNow()).WithMessage("The appointment must be in the future.");
    }
}
