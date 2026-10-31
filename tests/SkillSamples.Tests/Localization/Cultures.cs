using System.Globalization;

namespace SkillSamples.Localization;

public static class Cultures
{
    // ar-SA's default calendar comes from the machine's ICU: Umm al-Qura (Hijri) with Ubuntu 24.04's, Gregorian
    // with 26.04's. Both cultures set the calendar themselves, so a date reads the same on every machine.
    // Built on first use, not in a static initializer: on an image without ICU that would be a
    // TypeInitializationException on every use of the class; this way the error says what's missing.
    private static readonly Lazy<CultureInfo> Gregorian = new(() => ArabicWith(new GregorianCalendar(GregorianCalendarTypes.Localized)));
    private static readonly Lazy<CultureInfo> Hijri = new(() => ArabicWith(new UmAlQuraCalendar()));

    public static CultureInfo ArabicGregorian => Gregorian.Value;
    public static CultureInfo ArabicHijri => Hijri.Value;

    private static CultureInfo ArabicWith(Calendar calendar)
    {
        CultureInfo arSa;
        try
        {
            arSa = CultureInfo.GetCultureInfo("ar-SA");
        }
        catch (CultureNotFoundException ex)
        {
            throw new InvalidOperationException(
                "Arabic dates need the ar-SA culture, but the runtime has no ICU (globalization-invariant mode). " +
                "Add ICU to the runtime image.", ex);
        }

        var culture = (CultureInfo)arSa.Clone();
        culture.DateTimeFormat.Calendar = calendar;
        return CultureInfo.ReadOnly(culture);
    }
}
