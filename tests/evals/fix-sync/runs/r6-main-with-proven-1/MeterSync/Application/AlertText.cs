using System.Globalization;
using MeterSync.Domain;

namespace MeterSync.Application;

/// <summary>The SMS a patient gets for a reading out of range. Patients are in Riyadh and read Arabic.</summary>
public static class AlertText
{
    private const string RiyadhZoneId = "Asia/Riyadh";

    // The time is written as Gregorian dd/MM/yyyy HH:mm with Latin digits. ar-SA's default calendar comes
    // from the machine's ICU (Hijri on some images, Gregorian on others), so the invariant culture formats it.
    private const string WhenFormat = "dd/MM/yyyy HH:mm";

    private static readonly TimeZoneInfo Riyadh = ResolveRiyadh(id => TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone) ? zone : null);

    /// <summary>Writes the alert for a reading.</summary>
    /// <param name="reading">A reading with an alert.</param>
    /// <returns>The SMS text.</returns>
    public static string For(Reading reading)
    {
        var when = TimeZoneInfo.ConvertTime(reading.TakenAt, Riyadh).ToString(WhenFormat, CultureInfo.InvariantCulture);
        var level = reading.Alert == AlertKind.Low ? "منخفضة" : "مرتفعة";
        return $"قراءة السكر {reading.Raw} {reading.Unit} {level} ({when}). إذا كنت تشعر بتعب، تواصل مع طبيبك.";
    }

    // Images without tzdata have no Asia/Riyadh; Saudi Arabia is UTC+3 all year (no DST).
    internal static TimeZoneInfo ResolveRiyadh(Func<string, TimeZoneInfo?> findSystemZone) =>
        findSystemZone(RiyadhZoneId)
        ?? TimeZoneInfo.CreateCustomTimeZone(RiyadhZoneId, TimeSpan.FromHours(3), "Riyadh", "Arabia Standard Time");
}
