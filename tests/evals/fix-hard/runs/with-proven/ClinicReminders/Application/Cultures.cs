using System.Globalization;

namespace ClinicReminders.Application;

public static class Cultures
{
    // ar-SA's default calendar comes from the machine's ICU: Umm al-Qura (Hijri) with one version, Gregorian
    // with another. Setting the calendar here makes the SMS date read the same on every machine. Needs ICU
    // (see GlobalizationGuard).
    public static readonly CultureInfo ArabicGregorian = ArabicWith(new GregorianCalendar(GregorianCalendarTypes.Localized));

    private static CultureInfo ArabicWith(Calendar calendar)
    {
        var culture = (CultureInfo)CultureInfo.GetCultureInfo("ar-SA").Clone();
        culture.DateTimeFormat.Calendar = calendar;
        return CultureInfo.ReadOnly(culture);
    }
}
