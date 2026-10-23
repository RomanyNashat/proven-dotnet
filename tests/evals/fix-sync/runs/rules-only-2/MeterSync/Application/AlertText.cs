using System.Globalization;
using MeterSync.Domain;

namespace MeterSync.Application;

/// <summary>The SMS a patient gets for a reading out of range. Patients are in Riyadh and read Arabic.</summary>
public static class AlertText
{
    // Riyadh is UTC+3 all year (no daylight saving). A fixed offset needs no tzdata, which slim
    // images don't have: FindSystemTimeZoneById("Asia/Riyadh") throws there.
    private static readonly TimeSpan RiyadhOffset = TimeSpan.FromHours(3);

    /// <summary>Builds the text for an out-of-range reading.</summary>
    /// <param name="reading">The reading; its <see cref="Reading.Alert"/> says low or high.</param>
    /// <returns>The Arabic SMS, with the time in Riyadh on the Gregorian calendar.</returns>
    public static string For(Reading reading)
    {
        // Invariant culture, not ar-SA: ar-SA's calendar depends on the machine's ICU (Hijri on some, so the
        // patient read 1448 as the year) and the culture doesn't exist at all without ICU.
        var when = reading.TakenAt.ToOffset(RiyadhOffset).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
        var level = reading.Alert == AlertKind.Low ? "منخفضة" : "مرتفعة";
        return $"قراءة السكر {reading.Raw} {reading.Unit} {level} ({when}). إذا كنت تشعر بتعب، تواصل مع طبيبك.";
    }
}
