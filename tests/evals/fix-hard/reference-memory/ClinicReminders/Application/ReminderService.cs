using System.Globalization;
using ClinicReminders.Domain;
using ClinicReminders.Infrastructure;
using Microsoft.Extensions.Logging;

namespace ClinicReminders.Application;

public sealed record ScheduleReminder(string Mobile, DateTimeOffset AppointmentAt);

public sealed class ReminderService(
    RemindersDbContext db, SmsClient sms, TimeProvider time, ICurrentUser user, ILogger<ReminderService> logger)
{
    public async Task<int> ScheduleAsync(ScheduleReminder request, CancellationToken ct)
    {
        // The patient is the signed-in user, never a value from the body.
        var reminder = new Reminder(user.Id, request.Mobile, request.AppointmentAt, time.GetUtcNow());
        db.Reminders.Add(reminder);

        db.Audit.Add(new AuditEntry { ActorId = user.Id, Action = "reminder.scheduled", PatientId = user.Id, At = time.GetUtcNow() });
        await db.SaveChangesAsync(ct);
        return reminder.Id;
    }

    /// <summary>Sends the SMS. Returns false when the provider refused it.</summary>
    public async Task<bool> SendAsync(Reminder reminder, CancellationToken ct)
    {
        var local = TimeZoneInfo.ConvertTime(reminder.AppointmentAt, ClinicTime.Riyadh);
        var text = string.Format(ClinicTime.ArabicGregorian, "موعدك يوم {0:dddd d MMMM yyyy} الساعة {0:HH:mm}", local);
        try
        {
            await sms.SendAsync(reminder.Mobile, text, ct);
            ReminderLog.Sent(logger, reminder.Id, reminder.Mobile);
            return true;
        }
        catch (HttpRequestException ex)
        {
            // Not the exception: the provider's message can echo the mobile number.
            logger.LogError("Reminder {ReminderId} failed: {StatusCode}", reminder.Id, ex.StatusCode);
            return false;
        }
    }

    public string Reference(Reminder reminder) =>
        string.Create(CultureInfo.InvariantCulture, $"REM-{reminder.AppointmentAt:yyyyMMdd}-{reminder.Id}");
}

public interface ICurrentUser
{
    int Id { get; }
}
