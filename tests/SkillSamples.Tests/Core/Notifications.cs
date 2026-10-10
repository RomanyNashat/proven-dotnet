using Microsoft.Extensions.DependencyInjection;

namespace SkillSamples.Core;

public interface INotificationSender
{
    string Channel { get; }
}

public sealed class EmailSender : INotificationSender { public string Channel => "email"; }
public sealed class SmsSender : INotificationSender { public string Channel => "sms"; }

public sealed class AppointmentReminders(
    [FromKeyedServices("email")] INotificationSender email,
    [FromKeyedServices("sms")] INotificationSender sms)
{
    public IReadOnlyList<string> Channels => [email.Channel, sms.Channel];
}

public static class NotificationSetup
{
    public static IServiceCollection AddNotifications(this IServiceCollection services) =>
        services
            .AddKeyedSingleton<INotificationSender, EmailSender>("email")
            .AddKeyedSingleton<INotificationSender, SmsSender>("sms")
            .AddSingleton<AppointmentReminders>();
}
