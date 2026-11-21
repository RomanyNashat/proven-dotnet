using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.DependencyInjection;

namespace SkillSamples.Upgrade;

// On 8 and 9 a JSON null was read as "", so "Retries": null failed the bind and the service didn't start.
// On 10 it is null: "Retries" binds as 0 and "Sender" as null, over an earlier file and the defaults
// below, and the service starts. An environment variable set to "" is a value too, and wins over the
// JSON. The defaults don't protect a setting; the validation does.
public sealed class ReminderSettings
{
    public const string Section = "Reminders";

    [Required(AllowEmptyStrings = false)]
    public string Sender { get; set; } = "Clinic";

    [Range(1, 10)]
    public int Retries { get; set; } = 3;
}

public static class ReminderSetup
{
    public static IServiceCollection AddReminders(this IServiceCollection services)
    {
        services.AddOptions<ReminderSettings>()
            .BindConfiguration(ReminderSettings.Section)
            .ValidateDataAnnotations()
            .ValidateOnStart();   // a null or empty setting stops the deploy, not the first reminder
        return services;
    }
}
