namespace ClinicBooking.Notifications;

public interface IPatientPreferences
{
    Task<ContactPreference> ForAsync(int patientId, CancellationToken ct);
}

// Each patient picks how they're reminded; the channel is chosen per patient at runtime.
public sealed class ReminderSender(IEnumerable<INotificationChannel> channels, IPatientPreferences preferences)
{
    private readonly Dictionary<ContactPreference, INotificationChannel> _byPreference = channels.ToDictionary(c => c.Handles);

    public async Task RemindAsync(int patientId, string message, CancellationToken ct)
    {
        var preference = await preferences.ForAsync(patientId, ct);
        await _byPreference[preference].SendAsync(patientId, message, ct);
    }
}
