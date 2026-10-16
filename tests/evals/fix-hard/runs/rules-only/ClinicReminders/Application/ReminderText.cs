using System.Globalization;

namespace ClinicReminders.Application;

/// <summary>The SMS a patient reads, in Arabic.</summary>
public static class ReminderText
{
    // Written out instead of taken from the ar-SA culture: ar-SA's default calendar is Hijri (Um Al Qura) on
    // some ICU builds and Gregorian on others, and the culture doesn't exist at all in invariant mode. The
    // appointment is a Gregorian date, and the text says so ("م").
    private static readonly string[] Days = ["الأحد", "الاثنين", "الثلاثاء", "الأربعاء", "الخميس", "الجمعة", "السبت"];

    private static readonly string[] Months =
        ["يناير", "فبراير", "مارس", "أبريل", "مايو", "يونيو", "يوليو", "أغسطس", "سبتمبر", "أكتوبر", "نوفمبر", "ديسمبر"];

    /// <summary>The reminder for an appointment, in Riyadh time and the Gregorian calendar.</summary>
    /// <param name="appointmentAt">The appointment instant.</param>
    /// <returns>For example "موعدك يوم الخميس 15 أكتوبر 2026م الساعة 09:30".</returns>
    public static string ForAppointment(DateTimeOffset appointmentAt)
    {
        var local = TimeZoneInfo.ConvertTime(appointmentAt, RiyadhTime.Zone);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"موعدك يوم {Days[(int)local.DayOfWeek]} {local.Day} {Months[local.Month - 1]} {local.Year}م الساعة {local:HH:mm}");
    }
}
