using System.Globalization;
using MeterSync.Domain;

namespace MeterSync.Application;

/// <summary>The SMS a patient gets for a reading out of range. Patients are in Riyadh and read Arabic.</summary>
public static class AlertText
{
    // Riyadh is UTC+3 all year, so no time-zone database is needed (a slim image has none).
    private static readonly TimeSpan RiyadhOffset = TimeSpan.FromHours(3);

    private static readonly CultureInfo Arabic = Gregorian("ar-SA");

    public static string For(Reading reading)
    {
        var when = reading.TakenAt.ToOffset(RiyadhOffset).ToString("dd/MM/yyyy HH:mm", Arabic);
        var level = reading.Alert == AlertKind.Low ? "منخفضة" : "مرتفعة";
        return $"قراءة السكر {reading.Raw} {reading.Unit} {level} ({when}). إذا كنت تشعر بتعب، تواصل مع طبيبك.";
    }

    // ar-SA's default calendar comes from the machine's ICU, Hijri on some images: name the calendar.
    private static CultureInfo Gregorian(string name)
    {
        var culture = (CultureInfo)CultureInfo.GetCultureInfo(name).Clone();
        culture.DateTimeFormat.Calendar = new GregorianCalendar();
        return culture;
    }
}
