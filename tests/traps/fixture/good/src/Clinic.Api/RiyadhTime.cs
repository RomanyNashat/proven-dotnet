namespace Clinic.Api;

public static class RiyadhTime
{
    public static readonly TimeZoneInfo Zone = Find();
    public static readonly CultureInfo ArabicGregorian = Arabic();

    private static TimeZoneInfo Find()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.CreateCustomTimeZone("Riyadh", TimeSpan.FromHours(3), "Riyadh", "Riyadh");
        }
    }

    private static CultureInfo Arabic()
    {
        var culture = (CultureInfo)CultureInfo.GetCultureInfo("ar-SA").Clone();
        culture.DateTimeFormat.Calendar = new GregorianCalendar();
        return CultureInfo.ReadOnly(culture);
    }
}
