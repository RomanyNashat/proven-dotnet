using Microsoft.Extensions.DependencyInjection;

namespace SkillSamples.Patterns;

public enum NotificationChannel { Sms, Email, Push }

public interface INotificationSender
{
    NotificationChannel Channel { get; }
    Task SendAsync(string to, string text, CancellationToken ct);
}

// The router picks a strategy at runtime from the channel the caller asks for. Building the map in the
// constructor means a second sender for the same channel fails at startup, not on the first message.
public sealed class NotificationRouter
{
    private readonly IReadOnlyDictionary<NotificationChannel, INotificationSender> _senders;

    public NotificationRouter(IEnumerable<INotificationSender> senders) =>
        _senders = senders.ToDictionary(s => s.Channel);

    public Task SendAsync(NotificationChannel channel, string to, string text, CancellationToken ct) =>
        _senders.TryGetValue(channel, out var sender)
            ? sender.SendAsync(to, text, ct)
            : throw new NotSupportedException($"No sender registered for {channel}.");
}

// When the choice is fixed at compile time, a keyed service is enough: no router, no switch.
//   services.AddKeyedSingleton<INotificationSender, SmsSender>(NotificationChannel.Sms);
public sealed class OtpService([FromKeyedServices(NotificationChannel.Sms)] INotificationSender sms)
{
    public Task SendCodeAsync(string mobile, string code, CancellationToken ct) =>
        sms.SendAsync(mobile, $"Your code is {code}", ct);
}
