using System.Globalization;
using MeterSync.Domain;

namespace MeterSync.Application;

/// <summary>The SMS a patient gets for a reading out of range. Patients are in Riyadh and read Arabic.</summary>
public static class AlertText
{
    // Saudi Arabia is UTC+3 all year (no daylight saving). A fixed offset avoids depending on the
    // container having tzdata for "Asia/Riyadh".
    private static readonly TimeSpan RiyadhOffset = TimeSpan.FromHours(3);

    /// <summary>The Arabic alert text for a reading.</summary>
    /// <param name="reading">An out-of-range reading.</param>
    /// <returns>The SMS text.</returns>
    public static string For(Reading reading)
    {
        // Never format with ar-SA: its default calendar is Hijri (Umm al-Qura) or Gregorian depending on the
        // machine's ICU, so "dd/MM/yyyy" could print 1448 or 2026. Format the digits invariantly and name the
        // calendar: "م" marks a Gregorian (ميلادي) date.
        var local = reading.TakenAt.ToOffset(RiyadhOffset);
        var date = local.ToString("d/M/yyyy", CultureInfo.InvariantCulture);
        var clock = local.ToString("HH:mm", CultureInfo.InvariantCulture);
        var level = reading.Alert == AlertKind.Low ? "منخفضة" : "مرتفعة";
        var value = reading.Raw switch
        {
            "HI" => "أعلى من قدرة الجهاز على القياس",
            "LO" => "أقل من قدرة الجهاز على القياس",
            _ => $"{reading.Raw} {reading.Unit}",
        };
        return $"قراءة السكر {level}: {value} (بتاريخ {date}م الساعة {clock} بتوقيت الرياض). إذا كنت تشعر بتعب، تواصل مع طبيبك.";
    }
}
