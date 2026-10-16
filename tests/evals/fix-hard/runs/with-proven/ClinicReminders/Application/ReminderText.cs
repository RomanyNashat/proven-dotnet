namespace ClinicReminders.Application;

public static class ReminderText
{
    // Riyadh time, Arabic month names on the Gregorian calendar. Kept short: one Arabic SMS holds 70 characters.
    public static string For(DateTimeOffset appointmentAt)
    {
        var local = TimeZoneInfo.ConvertTime(appointmentAt, RiyadhTime.Zone);
        return string.Format(Cultures.ArabicGregorian, "موعدك يوم {0:dddd d MMMM yyyy} الساعة {0:HH:mm}", local);
    }
}
