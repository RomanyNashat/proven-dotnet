using System.Globalization;

namespace SkillSamples.Localization;

public static class Cultures
{
    // ar-SA's default calendar comes from the machine's ICU: Umm al-Qura (Hijri) with Ubuntu 24.04's, Gregorian
    // with 26.04's. Both cultures set the calendar themselves, so a date reads the same on every machine.
    public static readonly CultureInfo ArabicGregorian = ArabicWith(new GregorianCalendar(GregorianCalendarTypes.Localized));
    public static readonly CultureInfo ArabicHijri = ArabicWith(new UmAlQuraCalendar());

    private static CultureInfo ArabicWith(Calendar calendar)
    {
        var culture = (CultureInfo)CultureInfo.GetCultureInfo("ar-SA").Clone();
        culture.DateTimeFormat.Calendar = calendar;
        return CultureInfo.ReadOnly(culture);
    }
}
