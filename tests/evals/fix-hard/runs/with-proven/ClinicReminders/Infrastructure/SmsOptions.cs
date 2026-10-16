using System.ComponentModel.DataAnnotations;

namespace ClinicReminders.Infrastructure;

public sealed class SmsOptions
{
    public const string Section = "Sms";

    // `required` only binds the compiler: when the setting is missing, the binder leaves this null.
    // [Required] is what catches it, at start-up (ValidateOnStart).
    [Required]
    public required Uri BaseAddress { get; init; }
}
