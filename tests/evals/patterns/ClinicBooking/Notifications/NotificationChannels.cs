namespace ClinicBooking.Notifications;

public enum ContactPreference { Sms, Email }

public interface INotificationChannel
{
    ContactPreference Handles { get; }

    Task SendAsync(int patientId, string message, CancellationToken ct);
}

public sealed class SmsChannel(ISmsGateway sms) : INotificationChannel
{
    public ContactPreference Handles => ContactPreference.Sms;

    public Task SendAsync(int patientId, string message, CancellationToken ct) => sms.SendAsync(patientId, message, ct);
}

public sealed class EmailChannel(IEmailGateway email) : INotificationChannel
{
    public ContactPreference Handles => ContactPreference.Email;

    public Task SendAsync(int patientId, string message, CancellationToken ct) => email.SendAsync(patientId, "Appointment reminder", message, ct);
}

public interface ISmsGateway
{
    Task SendAsync(int patientId, string text, CancellationToken ct);
}

public interface IEmailGateway
{
    Task SendAsync(int patientId, string subject, string body, CancellationToken ct);
}
