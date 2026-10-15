using System.Globalization;
using ClinicReminders.Domain;
using ClinicReminders.Infrastructure;
using Microsoft.Extensions.Logging;

namespace ClinicReminders.Application;

public sealed record ScheduleReminder(int PatientId, string Mobile, DateTimeOffset AppointmentAt);

public sealed class ReminderService(
    RemindersDbContext db, SmsClient sms, TimeProvider time, ICurrentUser user, ILogger<ReminderService> logger)
{
    private static readonly TimeZoneInfo Riyadh = TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh");

    public async Task<int> ScheduleAsync(ScheduleReminder request, CancellationToken ct)
    {
        var reminder = new Reminder(request.PatientId, request.Mobile, request.AppointmentAt, time.GetUtcNow());
        db.Reminders.Add(reminder);

        db.Audit.Add(new AuditEntry { ActorId = user.Id, Action = "reminder.scheduled", PatientId = request.PatientId, At = time.GetUtcNow() });
        await db.SaveChangesAsync(ct);
        return reminder.Id;
    }

    public async Task SendAsync(Reminder reminder, CancellationToken ct)
    {
        var local = TimeZoneInfo.ConvertTime(reminder.AppointmentAt, Riyadh);
        var text = string.Format(new CultureInfo("ar-SA"), "موعدك يوم {0:dddd d MMMM yyyy} الساعة {0:HH:mm}", local);
        try
        {
            await sms.SendAsync(reminder.Mobile, text, ct);
            reminder.MarkSent(time.GetUtcNow());
            ReminderLog.Sent(logger, reminder.Id, reminder.Mobile);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Reminder {ReminderId} failed", reminder.Id);
        }
    }

    public string Reference(Reminder reminder) =>
        string.Create(CultureInfo.InvariantCulture, $"REM-{reminder.AppointmentAt:yyyyMMdd}-{reminder.Id}");
}

public interface ICurrentUser
{
    int Id { get; }
}
