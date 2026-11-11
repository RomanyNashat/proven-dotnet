using System.Globalization;
using MeterSync.Domain;

namespace MeterSync.Application;

/// <summary>The SMS a patient gets for a reading out of range. Patients are in Riyadh and read Arabic.</summary>
public static class AlertText
{
    // Literal separators: the culture's "/" placeholder brings in ar-SA's right-to-left mark. The digits
    // stay Latin because .NET formatting never substitutes native digits.
    private const string WhenFormat = "dd'/'MM'/'yyyy HH':'mm";

    // ar-SA defaults to the Um Al-Qura (Hijri) calendar, or Gregorian, depending on the machine's ICU;
    // pin Gregorian so every pod sends the same date, and say so in the text.
    private static readonly CultureInfo Arabic = GregorianArabic();

    private static readonly Lazy<TimeZoneInfo> Riyadh = new(() => ResolveRiyadh(TimeZoneInfo.FindSystemTimeZoneById));

    /// <summary>The Arabic SMS for an out-of-range reading.</summary>
    /// <param name="reading">The reading to alert about.</param>
    /// <returns>The message text.</returns>
    public static string For(Reading reading)
    {
        var when = TimeZoneInfo.ConvertTime(reading.TakenAt, Riyadh.Value).ToString(WhenFormat, Arabic);
        var what = (reading.ValueMgDl, reading.Alert) switch
        {
            (null, AlertKind.Low) => "أقل من نطاق جهاز القياس",
            (null, _) => "أعلى من نطاق جهاز القياس",
            (_, AlertKind.Low) => $"{reading.Raw} {reading.Unit} منخفضة",
            _ => $"{reading.Raw} {reading.Unit} مرتفعة",
        };
        return $"قراءة السكر {what} ({when} بتوقيت الرياض، تقويم ميلادي). إذا كنت تشعر بتعب، تواصل مع طبيبك.";
    }

    internal static TimeZoneInfo ResolveRiyadh(Func<string, TimeZoneInfo> find)
    {
        try
        {
            return find("Asia/Riyadh");
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            // A container image without tzdata; Saudi Arabia has no daylight saving, so a fixed +3 is exact.
            return TimeZoneInfo.CreateCustomTimeZone("Asia/Riyadh", TimeSpan.FromHours(3), "Arabia Standard Time", "Arabia Standard Time");
        }
    }

    private static CultureInfo GregorianArabic()
    {
        var culture = (CultureInfo)CultureInfo.GetCultureInfo("ar-SA").Clone();
        culture.DateTimeFormat.Calendar = new GregorianCalendar();
        return culture;
    }
}
