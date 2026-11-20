using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.DependencyInjection;

namespace SkillSamples.Upgrade;

// On .NET 10 a JSON null is a value: "Retries": null in appsettings.Production.json binds as 0, not as the
// default below, and "Sender": null as null. An environment variable set to "" is a value too, and wins
// over the JSON. The defaults don't protect a setting; the validation does.
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
