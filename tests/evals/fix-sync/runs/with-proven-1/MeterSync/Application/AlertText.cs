using System.Globalization;
using MeterSync.Domain;

namespace MeterSync.Application;

/// <summary>The SMS a patient gets for a reading out of range. Patients are in Riyadh and read Arabic.</summary>
public static class AlertText
{
    // Saudi Arabia is UTC+3 all year with no daylight saving, so a fixed offset is exact and needs no tzdata.
    private static readonly TimeSpan Riyadh = TimeSpan.FromHours(3);

    public static string For(Reading reading)
    {
        // The invariant culture: Gregorian calendar and Latin digits on every image. Any ar-* culture needs
        // ICU (it throws without it) and its calendar is whatever the machine's ICU says (Hijri on some).
        var when = reading.TakenAt.ToOffset(Riyadh).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
        var level = reading.Alert == AlertKind.Low ? "منخفضة" : "مرتفعة";
        return $"قراءة السكر {reading.Raw} {reading.Unit} {level} ({when}). إذا كنت تشعر بتعب، تواصل مع طبيبك.";
    }
}
