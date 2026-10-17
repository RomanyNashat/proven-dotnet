namespace ClinicReminders.Application;

public static class ReminderMessage
{
    // Short on purpose: one Arabic SMS part holds 70 characters.
    private const string ArabicTemplate = "موعدك يوم {0:dddd d MMMM yyyy} الساعة {0:HH:mm}";

    /// <summary>The patient's SMS: Riyadh time, Arabic month names on the Gregorian calendar.</summary>
    public static string Arabic(DateTimeOffset appointmentAt) =>
        string.Format(Cultures.ArabicGregorian, ArabicTemplate, TimeZoneInfo.ConvertTime(appointmentAt, RiyadhTime.Zone));
}
