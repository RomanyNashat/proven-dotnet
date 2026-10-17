using System.Globalization;

namespace ClinicReminders.Application;

public static class Cultures
{
    // ar-SA's default calendar comes from the machine's ICU: Umm al-Qura (Hijri) on one image, Gregorian on
    // another. This one sets the calendar itself, so a date reads the same on every machine. Needs ICU.
    public static readonly CultureInfo ArabicGregorian = ArabicWith(new GregorianCalendar(GregorianCalendarTypes.Localized));

    private static CultureInfo ArabicWith(Calendar calendar)
    {
        var culture = (CultureInfo)CultureInfo.GetCultureInfo("ar-SA").Clone();
        culture.DateTimeFormat.Calendar = calendar;
        return CultureInfo.ReadOnly(culture);
    }
}
