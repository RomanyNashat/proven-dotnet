using System.Globalization;
using ClinicReminders.Domain;
using ClinicReminders.Infrastructure;

namespace ClinicReminders.Application;

public sealed class ReminderService(RemindersDbContext db, TimeProvider time, ICurrentUser user)
{
    public async Task<ScheduleResult> ScheduleAsync(ScheduleReminder request, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var errors = request.Validate(now);
        if (errors.Count > 0)
        {
            return new ScheduleResult.Invalid(errors);
        }

        // The id in the body is the caller's to use only if it is their own (or they are clinic staff):
        // otherwise anyone signed in could put reminders on another patient's list and text any number.
        if (request.PatientId != user.Id && !user.IsStaff)
        {
            return new ScheduleResult.Forbidden();
        }

        var reminder = new Reminder(request.PatientId, request.Mobile.Trim(), request.AppointmentAt, now);
        db.Reminders.Add(reminder);
        db.Audit.Add(new AuditEntry { ActorId = user.Id, Action = "reminder.scheduled", PatientId = request.PatientId, At = now });
        await db.SaveChangesAsync(ct);
        return new ScheduleResult.Scheduled(reminder.Id);
    }

    /// <summary>A reference for the reminder, dated by the appointment's day in Riyadh (not its UTC day).</summary>
    public static string Reference(Reminder reminder) =>
        string.Create(CultureInfo.InvariantCulture, $"REM-{ClinicTime.ToRiyadh(reminder.AppointmentAt):yyyyMMdd}-{reminder.Id}");
}
