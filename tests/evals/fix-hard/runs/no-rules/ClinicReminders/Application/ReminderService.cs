using System.Globalization;
using ClinicReminders.Domain;
using ClinicReminders.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClinicReminders.Application;

public sealed record ScheduleReminder(int PatientId, string Mobile, DateTimeOffset AppointmentAt);

public static class ClinicTime
{
    // Saudi Arabia has no DST (fixed UTC+3), so the fallback is exact when tzdata is missing from the image.
    public static readonly TimeZoneInfo Riyadh = Find();

    private static TimeZoneInfo Find()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.CreateCustomTimeZone("Asia/Riyadh", TimeSpan.FromHours(3), "Riyadh", "Riyadh");
        }
    }
}

public sealed class ReminderService(
    RemindersDbContext db, SmsClient sms, TimeProvider time, ICurrentUser user, ILogger<ReminderService> logger)
{
    public async Task<int> ScheduleAsync(ScheduleReminder request, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var reminder = new Reminder(request.PatientId, request.Mobile, request.AppointmentAt, now);
        db.Reminders.Add(reminder);

        db.Audit.Add(new AuditEntry { ActorId = user.Id, Action = "reminder.scheduled", PatientId = request.PatientId, At = now });
        await db.SaveChangesAsync(ct);
        return reminder.Id;
    }

    // The SMS provider has no idempotency key, so a message must never be sent twice for one reminder.
    // We therefore claim the row first (atomic compare-and-set on SentAt) and only then send: concurrent
    // runs or instances cannot both win, and a crash after the claim loses one reminder rather than
    // texting a patient repeatedly. If the provider explicitly rejects the request the claim is released
    // so the next run retries; if the outcome is unknown (timeout/connection drop) it stays claimed.
    public async Task SendAsync(Reminder reminder, CancellationToken ct)
    {
        DateTimeOffset? claimedAt = time.GetUtcNow();
        var claimed = await db.Reminders
            .Where(r => r.Id == reminder.Id && r.SentAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.SentAt, claimedAt), ct);
        if (claimed == 0)
        {
            return;
        }

        try
        {
            await sms.SendAsync(reminder.Mobile, BuildText(reminder.AppointmentAt), ct);
            ReminderLog.Sent(logger, reminder.Id);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is not null)
        {
            logger.LogError(ex, "Reminder {ReminderId} rejected by SMS provider; will retry on the next run", reminder.Id);
            await Release(reminder.Id);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            logger.LogError(ex, "Reminder {ReminderId} delivery unknown (no response); not retried to avoid a duplicate SMS", reminder.Id);
        }
    }

    private Task<int> Release(int id) =>
        db.Reminders.Where(r => r.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.SentAt, default(DateTimeOffset?)), CancellationToken.None);

    private static string BuildText(DateTimeOffset appointmentAt)
    {
        var local = TimeZoneInfo.ConvertTime(appointmentAt, ClinicTime.Riyadh);
        // ar-SA defaults to the Hijri calendar; appointments are booked in Gregorian dates.
        var culture = new CultureInfo("ar-SA");
        culture.DateTimeFormat.Calendar = new GregorianCalendar();
        return string.Format(culture, "موعدك يوم {0:dddd d MMMM yyyy} الساعة {0:HH:mm}", local);
    }
}

public interface ICurrentUser
{
    int Id { get; }
}
