using System.Globalization;

namespace SkillSamples.Localization;

public static class Cultures
{
    public static readonly CultureInfo ArabicGregorian = CreateArabicGregorian();
    public static readonly CultureInfo ArabicHijri = CultureInfo.ReadOnly(new CultureInfo("ar-SA"));

    private static CultureInfo CreateArabicGregorian()
    {
        var culture = (CultureInfo)CultureInfo.GetCultureInfo("ar-SA").Clone();
        culture.DateTimeFormat.Calendar = new GregorianCalendar(GregorianCalendarTypes.Localized);
        return CultureInfo.ReadOnly(culture);
    }
}
