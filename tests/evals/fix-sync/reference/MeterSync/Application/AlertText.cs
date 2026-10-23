using System.Globalization;
using MeterSync.Domain;

namespace MeterSync.Application;

/// <summary>The SMS a patient gets for a reading out of range. Patients are in Riyadh and read Arabic.</summary>
public static class AlertText
{
    // A slim image has no tzdata, and FindSystemTimeZoneById then throws. Riyadh has no daylight saving.
    private static readonly TimeZoneInfo Riyadh = FindRiyadh();

    public static string For(Reading reading)
    {
        // The date in the Gregorian calendar and Western digits: ar-SA's calendar depends on the machine's ICU.
        var when = TimeZoneInfo.ConvertTime(reading.TakenAt, Riyadh).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
        var level = reading.Alert == AlertKind.Low ? "منخفضة" : "مرتفعة";
        return $"قراءة السكر {reading.Raw} {reading.Unit} {level} ({when}). إذا كنت تشعر بتعب، تواصل مع طبيبك.";
    }

    private static TimeZoneInfo FindRiyadh()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.CreateCustomTimeZone("Asia/Riyadh", TimeSpan.FromHours(3), "Riyadh", "Riyadh");
        }
    }
}
