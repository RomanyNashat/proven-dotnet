using System.Globalization;
using MeterSync.Domain;

namespace MeterSync.Application;

/// <summary>The SMS a patient gets for a reading out of range. Patients are in Riyadh and read Arabic.</summary>
public static class AlertText
{
    public static string For(Reading reading)
    {
        // Gregorian digits only, so the invariant culture writes exactly what an Arabic Gregorian culture would,
        // without depending on ICU. ar-SA would throw on an image without ICU, and with ICU its calendar is
        // Hijri on some images and Gregorian on others, under a format that looks Gregorian.
        var when = TimeZoneInfo.ConvertTime(reading.TakenAt, RiyadhTime.Zone).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
        var level = reading.Alert == AlertKind.Low ? "منخفضة" : "مرتفعة";
        return $"قراءة السكر {reading.Raw} {reading.Unit} {level} ({when}). إذا كنت تشعر بتعب، تواصل مع طبيبك.";
    }
}
