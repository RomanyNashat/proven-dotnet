using System.Globalization;
using MeterSync.Domain;

namespace MeterSync.Application;

/// <summary>The SMS a patient gets for a reading out of range. Patients are in Riyadh and read Arabic.</summary>
public static class AlertText
{
    // Riyadh is UTC+3 all year with no daylight saving. A fixed offset doesn't depend on tzdata being
    // installed in the container, and the invariant culture keeps the Gregorian calendar (ar-SA would
    // print Hijri dates) and doesn't need ICU.
    private static readonly TimeSpan Riyadh = TimeSpan.FromHours(3);

    public static string For(Reading reading)
    {
        var when = reading.TakenAt.ToOffset(Riyadh).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
        var level = reading.Alert == AlertKind.Low ? "منخفضة" : "مرتفعة";
        return $"قراءة السكر {reading.Raw} {reading.Unit} {level} ({when}). إذا كنت تشعر بتعب، تواصل مع طبيبك.";
    }
}
