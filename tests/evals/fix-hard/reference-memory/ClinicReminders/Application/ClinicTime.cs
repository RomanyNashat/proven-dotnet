using System.Globalization;

namespace ClinicReminders.Application;

public static class ClinicTime
{
    // Riyadh has had no daylight saving since 1980, so a fixed +03:00 is exact when tzdata is missing.
    public static readonly TimeZoneInfo Riyadh = FindRiyadh();

    // The patient reads a Gregorian date. ar-SA's default calendar depends on the machine's ICU.
    public static readonly CultureInfo ArabicGregorian = ArabicWithGregorian();

    private static TimeZoneInfo FindRiyadh()
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

    private static CultureInfo ArabicWithGregorian()
    {
        var culture = (CultureInfo)CultureInfo.GetCultureInfo("ar-SA").Clone();
        culture.DateTimeFormat.Calendar = new GregorianCalendar(GregorianCalendarTypes.Localized);
        return CultureInfo.ReadOnly(culture);
    }
}
