using ClinicReminders.Domain;
using ClinicReminders.Infrastructure;

namespace ClinicReminders.Application;

public sealed record ScheduleReminder(int PatientId, string Mobile, DateTimeOffset AppointmentAt);

public sealed class ReminderService(RemindersDbContext db, TimeProvider time, ICurrentUser user)
{
    /// <summary>Schedules a reminder and audits it, in one SaveChanges (one transaction).</summary>
    /// <param name="request">A request already checked by <see cref="ScheduleReminderRules.Validate"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The new reminder's id.</returns>
    public async Task<int> ScheduleAsync(ScheduleReminder request, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var reminder = new Reminder(
            request.PatientId, ScheduleReminderRules.NormalizeMobile(request.Mobile), request.AppointmentAt, now);
        db.Reminders.Add(reminder);

        db.Audit.Add(new AuditEntry { ActorId = user.Id, Action = "reminder.scheduled", PatientId = request.PatientId, At = now });
        await db.SaveChangesAsync(ct);
        return reminder.Id;
    }
}

public interface ICurrentUser
{
    int Id { get; }
}
