using System.Globalization;
using ClinicReminders.Domain;
using ClinicReminders.Infrastructure;
using Microsoft.Extensions.Logging;

namespace ClinicReminders.Application;

public sealed record ScheduleReminder(int PatientId, string Mobile, DateTimeOffset AppointmentAt);

public sealed class ReminderService(
    RemindersDbContext db, SmsClient sms, TimeProvider time, ICurrentUser user, ILogger<ReminderService> logger)
{
    public static readonly TimeZoneInfo Riyadh = TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh");

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
        var text = MessageText(reminder.AppointmentAt);
        try
        {
            await sms.SendAsync(reminder.Mobile, text, ct);
            reminder.MarkSent(time.GetUtcNow());
            ReminderLog.Sent(logger, reminder.Id, reminder.Mobile);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            // Not marked sent, so the next run retries it; one bad send must not stop the rest of the batch.
            logger.LogError(ex, "Reminder {ReminderId} failed", reminder.Id);
        }
    }

    // Formatted by hand: the container runs with invariant globalization (no ICU), and ar-SA defaults to the Hijri calendar.
    private static readonly string[] Days = ["الأحد", "الاثنين", "الثلاثاء", "الأربعاء", "الخميس", "الجمعة", "السبت"];

    private static readonly string[] Months =
        ["يناير", "فبراير", "مارس", "أبريل", "مايو", "يونيو", "يوليو", "أغسطس", "سبتمبر", "أكتوبر", "نوفمبر", "ديسمبر"];

    public static string MessageText(DateTimeOffset appointmentAt)
    {
        var local = TimeZoneInfo.ConvertTime(appointmentAt, Riyadh);
        return string.Create(CultureInfo.InvariantCulture,
            $"موعدك يوم {Days[(int)local.DayOfWeek]} {local.Day} {Months[local.Month - 1]} {local.Year} الساعة {local:HH:mm}");
    }

    public string Reference(Reminder reminder) =>
        string.Create(CultureInfo.InvariantCulture, $"REM-{TimeZoneInfo.ConvertTime(reminder.AppointmentAt, Riyadh):yyyyMMdd}-{reminder.Id}");
}

public interface ICurrentUser
{
    int Id { get; }
}
