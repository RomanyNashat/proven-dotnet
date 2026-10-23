using System.Globalization;
using MeterSync.Domain;

namespace MeterSync.Application;

/// <summary>The SMS a patient gets for a reading out of range. Patients are in Riyadh and read Arabic.</summary>
public static class AlertText
{
    // Riyadh is UTC+3 all year (no daylight saving). A fixed offset needs no tzdata in the image, where
    // FindSystemTimeZoneById("Asia/Riyadh") throws on a slim image without it.
    private static readonly TimeSpan RiyadhOffset = TimeSpan.FromHours(3);

    /// <summary>The message for a reading.</summary>
    /// <param name="reading">A reading that raised an alert.</param>
    /// <returns>The Arabic message.</returns>
    public static string For(Reading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);

        // Gregorian, Western digits, formatted with the invariant culture: ar-SA's calendar depends on the
        // machine's ICU (Hijri on some images, so "02/11/2026" could come out as a Hijri date), and on an
        // image without ICU new CultureInfo("ar-SA") throws. The "م" after the year names the calendar.
        var local = reading.TakenAt.ToOffset(RiyadhOffset);
        var date = local.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        var time = local.ToString("HH:mm", CultureInfo.InvariantCulture);

        var level = reading.Alert == AlertKind.Low ? "منخفضة" : "مرتفعة";
        var value = reading.Raw.Trim().ToUpperInvariant() switch
        {
            "HI" => "أعلى من نطاق قياس الجهاز",
            "LO" => "أقل من نطاق قياس الجهاز",
            _ => $"{reading.Raw} {reading.Unit}",
        };

        return $"قراءة السكر {level}: {value} (بتاريخ {date}م الساعة {time} بتوقيت الرياض). إذا كنت تشعر بتعب، تواصل مع طبيبك.";
    }
}
