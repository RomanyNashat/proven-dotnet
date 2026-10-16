using ClinicReminders.Domain;
using ClinicReminders.Infrastructure;

namespace ClinicReminders.Application;

public sealed record ScheduleReminder(int PatientId, string Mobile, DateTimeOffset AppointmentAt);

public sealed class ReminderService(RemindersDbContext db, TimeProvider time)
{
    // The request is already validated and its PatientId checked against the caller (ReminderEndpoints).
    public async Task<int> ScheduleAsync(int callerId, ScheduleReminder request, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var reminder = new Reminder(request.PatientId, request.Mobile, request.AppointmentAt, now);
        db.Reminders.Add(reminder);

        db.Audit.Add(new AuditEntry { ActorId = callerId, Action = "reminder.scheduled", PatientId = request.PatientId, At = now });
        await db.SaveChangesAsync(ct);
        return reminder.Id;
    }
}
