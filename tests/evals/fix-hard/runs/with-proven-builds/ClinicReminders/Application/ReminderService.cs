using ClinicReminders.Domain;
using ClinicReminders.Infrastructure;

namespace ClinicReminders.Application;

public sealed class ReminderService(RemindersDbContext db, TimeProvider time)
{
    /// <summary>Schedules a reminder for the calling patient; the request is validated before this.</summary>
    public async Task<int> ScheduleAsync(int patientId, ScheduleReminder request, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var reminder = new Reminder(patientId, request.Mobile, request.AppointmentAt, now);
        db.Reminders.Add(reminder);
        db.Audit.Add(new AuditEntry { ActorId = patientId, Action = "reminder.scheduled", PatientId = patientId, At = now });
        await db.SaveChangesAsync(ct);
        return reminder.Id;
    }
}
