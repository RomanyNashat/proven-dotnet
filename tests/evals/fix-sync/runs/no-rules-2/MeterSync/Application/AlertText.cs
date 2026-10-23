using System.Globalization;
using MeterSync.Domain;

namespace MeterSync.Application;

/// <summary>The SMS a patient gets for a reading out of range. Patients are in Riyadh and read Arabic.</summary>
public static class AlertText
{
    // Saudi Arabia has no DST; used when the container has no tz database.
    private static readonly TimeSpan RiyadhOffset = TimeSpan.FromHours(3);

    public static string For(Reading reading)
    {
        // Gregorian calendar and Western digits: ar-SA would format the date in the Hijri calendar.
        var when = ToRiyadh(reading.TakenAt).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
        var level = reading.Alert == AlertKind.Low ? "منخفضة" : "مرتفعة";
        return $"قراءة السكر {reading.Raw} {reading.Unit} {level} ({when}). إذا كنت تشعر بتعب، تواصل مع طبيبك.";
    }

    private static DateTime ToRiyadh(DateTimeOffset at)
    {
        try
        {
            return TimeZoneInfo.ConvertTime(at, TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh")).DateTime;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return at.ToOffset(RiyadhOffset).DateTime;
        }
    }
}
