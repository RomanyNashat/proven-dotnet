using System.Globalization;

namespace MeterSync.Application;

/// <summary>Arabic formatting with the calendar named, for text a patient reads.</summary>
public static class ArabicCulture
{
    // ar-SA's calendar comes from the machine's ICU: Umm al-Qura (Hijri) with Ubuntu 24.04's, Gregorian with newer
    // ones. Setting it here makes a date read the same on every pod. Built on first use, not in a static
    // initializer, so an image without ICU gets null (and a fallback format) instead of a TypeInitializationException.
    private static readonly Lazy<CultureInfo?> GregorianCulture = new(() => CreateGregorian("ar-SA"));

    /// <summary>ar-SA with the Gregorian calendar, or null when the runtime has no ICU (invariant globalization).</summary>
    public static CultureInfo? Gregorian => GregorianCulture.Value;

    internal static CultureInfo? CreateGregorian(string name)
    {
        try
        {
            var culture = (CultureInfo)CultureInfo.GetCultureInfo(name).Clone();
            culture.DateTimeFormat.Calendar = new GregorianCalendar(GregorianCalendarTypes.Localized);
            return CultureInfo.ReadOnly(culture);
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }
}
