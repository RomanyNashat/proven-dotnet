using System.Globalization;

namespace ClinicReminders.Application;

/// <summary>The Arabic SMS a patient reads.</summary>
/// <remarks>
/// Not formatted with the ar-SA culture: its calendar comes from the machine's ICU (Hijri Um Al-Qura on some
/// images, Gregorian on others), so the same appointment could read as "1448" on one pod and "2026" on another,
/// with no word saying which. The text is built from fixed Gregorian names, marks the year with "م" so the
/// calendar is explicit, and is the same on every machine (with or without ICU).
/// </remarks>
public static class ReminderText
{
    private static readonly string[] Days =
        ["الأحد", "الاثنين", "الثلاثاء", "الأربعاء", "الخميس", "الجمعة", "السبت"];

    private static readonly string[] Months =
        ["يناير", "فبراير", "مارس", "أبريل", "مايو", "يونيو", "يوليو", "أغسطس", "سبتمبر", "أكتوبر", "نوفمبر", "ديسمبر"];

    public static string For(DateTimeOffset appointmentAt)
    {
        var local = ClinicTime.ToRiyadh(appointmentAt);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"موعدك يوم {Days[(int)local.DayOfWeek]} {local.Day} {Months[local.Month - 1]} {local.Year}م الساعة {local:HH:mm}");
    }
}
