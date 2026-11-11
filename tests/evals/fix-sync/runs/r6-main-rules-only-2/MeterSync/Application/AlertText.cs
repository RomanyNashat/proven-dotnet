using System.Globalization;
using MeterSync.Domain;

namespace MeterSync.Application;

/// <summary>The SMS a patient gets for a reading out of range. Patients are in Riyadh and read Arabic.</summary>
/// <remarks>
/// The date is formatted with the invariant (Gregorian) culture and marked "م" (Gregorian). Formatting with
/// ar-SA would use the machine's ICU default calendar for it, which is Umm al-Qura (Hijri) on some images and
/// Gregorian on others, so the same reading would show 1448 on one pod and 2026 on another.
/// </remarks>
public static class AlertText
{
    // Riyadh is UTC+3 all year (no DST). The fallback covers container images without tzdata.
    private static readonly TimeZoneInfo Riyadh = FindRiyadh();

    /// <summary>The Arabic SMS text for an out-of-range reading.</summary>
    public static string For(Reading reading)
    {
        var local = TimeZoneInfo.ConvertTime(reading.TakenAt, Riyadh);
        var when = local.ToString("dd/MM/yyyy'م' HH:mm", CultureInfo.InvariantCulture);
        var level = reading.Alert == AlertKind.Low ? "منخفضة" : "مرتفعة";
        var value = ValueText(reading);
        return $"قراءة السكر {value} {level} ({when} بتوقيت الرياض). إذا كنت تشعر بتعب، تواصل مع طبيبك.";
    }

    private static string ValueText(Reading reading) => reading.Raw.ToUpperInvariant() switch
    {
        // HI/LO mean the meter's range was exceeded; spell out what that means rather than sending "HI".
        "HI" => "أعلى من 600 mg/dL",
        "LO" => "أقل من 20 mg/dL",
        _ => $"{reading.Raw} {reading.Unit}",
    };

    private static TimeZoneInfo FindRiyadh()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh");
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.CreateCustomTimeZone("Asia/Riyadh", TimeSpan.FromHours(3), "Riyadh", "Riyadh");
        }
    }
}
