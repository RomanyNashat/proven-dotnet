using System.Globalization;
using MeterSync.Domain;

namespace MeterSync.Application;

/// <summary>The SMS a patient gets for a reading out of range. Patients are in Riyadh and read Arabic.</summary>
public static class AlertText
{
    // Riyadh is UTC+3 all year with no DST, so the fixed zone is exact on images that ship without tzdata.
    private static readonly TimeZoneInfo Riyadh =
        TimeZoneInfo.TryFindSystemTimeZoneById("Asia/Riyadh", out var zone)
            ? zone
            : TimeZoneInfo.CreateCustomTimeZone("Asia/Riyadh", TimeSpan.FromHours(3), "Arabia Standard Time", "Arabia Standard Time");

    public static string For(Reading reading)
    {
        // ar-SA's calendar (Hijri or Gregorian) and digits depend on the machine's ICU; the invariant
        // culture gives the same Gregorian date in Latin digits on every pod.
        var when = TimeZoneInfo.ConvertTime(reading.TakenAt, Riyadh).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
        var level = reading.Alert == AlertKind.Low ? "منخفضة" : "مرتفعة";
        return $"قراءة السكر {reading.Raw} {reading.Unit} {level} ({when}). إذا كنت تشعر بتعب، تواصل مع طبيبك.";
    }
}
