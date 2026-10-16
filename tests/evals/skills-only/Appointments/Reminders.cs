using Hangfire;

namespace Appointments;

public interface IReminderSender
{
    Task SendAsync(int appointmentId, CancellationToken ct);
}

// An SMS must never go out twice: no retries.
[AutomaticRetry(Attempts = 0)]
public sealed class ReminderSender(ISmsGateway sms, AppointmentsDbContext db) : IReminderSender
{
    public async Task SendAsync(int appointmentId, CancellationToken ct)
    {
        var appointment = await db.Appointments.FindAsync([appointmentId], ct)
            ?? throw new InvalidOperationException($"No appointment {appointmentId}.");
        await sms.SendAsync(appointment.PatientId, $"Reminder: your appointment at {appointment.At:HH:mm}", ct);
    }
}

public interface ISmsGateway
{
    Task SendAsync(int patientId, string text, CancellationToken ct);
}

public sealed class SmsGateway(HttpClient http) : ISmsGateway
{
    public async Task SendAsync(int patientId, string text, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("messages", new { patientId, text }, ct);
        response.EnsureSuccessStatusCode();
    }
}

public sealed class ReminderScheduler(IBackgroundJobClient jobs)
{
    public void Schedule(int appointmentId, DateTimeOffset sendAt) =>
        jobs.Schedule<IReminderSender>(s => s.SendAsync(appointmentId, CancellationToken.None), sendAt);
}
