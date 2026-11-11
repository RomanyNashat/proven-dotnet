using System.Globalization;

namespace MeterSync.Application;

/// <summary>Arabic text on the Gregorian calendar, the same on every machine.</summary>
public static class ArabicCulture
{
    // ar-SA's default calendar comes from the machine's ICU: Umm al-Qura (Hijri) on some images,
    // Gregorian on others. Setting the calendar makes the SMS date read the same everywhere.
    // Built on first use, not in a static initializer: without ICU that would be a
    // TypeInitializationException on every use; this way the error says what's missing.
    private static readonly Lazy<CultureInfo> Gregorian = new(CreateGregorian);

    public static CultureInfo ArabicGregorian => Gregorian.Value;

    private static CultureInfo CreateGregorian()
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
        culture.DateTimeFormat.Calendar = new GregorianCalendar(GregorianCalendarTypes.Localized);
        return CultureInfo.ReadOnly(culture);
    }
}
