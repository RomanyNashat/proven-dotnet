using System.Globalization;
using MeterSync.Domain;

namespace MeterSync.Application;

/// <summary>The SMS a patient gets for a reading out of range. Patients are in Riyadh and read Arabic.</summary>
public static class AlertText
{
    // Riyadh is UTC+3 all year (no DST since 1947). A fixed offset avoids depending on the image having tzdata.
    private static readonly TimeSpan Riyadh = TimeSpan.FromHours(3);

    /// <summary>Builds the Arabic SMS text for an out-of-range reading.</summary>
    /// <param name="reading">The reading.</param>
    /// <returns>The text.</returns>
    public static string For(Reading reading)
    {
        // Not ar-SA: its default calendar is Hijri (Um Al-Qura) on some ICU builds and Gregorian on others,
        // so the same code would text "1448" on one pod and "2026" on another. Invariant digits, Gregorian
        // calendar, and the calendar named in the text.
        var local = reading.TakenAt.ToOffset(Riyadh);
        var date = local.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
        var clock = local.ToString("HH:mm", CultureInfo.InvariantCulture);

        var value = reading.Raw.ToUpperInvariant() switch
        {
            Reading.AboveRange => "أعلى من 600 mg/dL",
            Reading.BelowRange => "أقل من 20 mg/dL",
            _ => $"{reading.Raw} {reading.Unit}",
        };
        var level = reading.Alert == AlertKind.Low ? "منخفضة" : "مرتفعة";

        return $"قراءة السكر {value} {level} (يوم {date} ميلادي، الساعة {clock} بتوقيت الرياض). إذا كنت تشعر بتعب، تواصل مع طبيبك.";
    }
}
