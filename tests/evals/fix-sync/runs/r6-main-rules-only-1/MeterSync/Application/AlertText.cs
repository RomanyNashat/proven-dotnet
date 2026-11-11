using System.Globalization;
using MeterSync.Domain;

namespace MeterSync.Application;

/// <summary>The SMS a patient gets for a reading out of range. Patients are in Riyadh and read Arabic.</summary>
public static class AlertText
{
    // Riyadh is UTC+3 all year (no daylight saving). A fixed offset avoids depending on tzdata being in the image.
    private static readonly TimeSpan RiyadhOffset = TimeSpan.FromHours(3);

    /// <summary>Builds the Arabic alert text.</summary>
    /// <param name="reading">An out-of-range reading.</param>
    /// <returns>The SMS body.</returns>
    public static string For(Reading reading)
    {
        // Formatted with the invariant (Gregorian) calendar and named as such: ar-SA's calendar depends on
        // the machine's ICU (Hijri on some images), which would turn 2026 into a Hijri year.
        var local = reading.TakenAt.ToOffset(RiyadhOffset);
        var date = local.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        var time = local.ToString("HH:mm", CultureInfo.InvariantCulture);
        var level = reading.Alert == AlertKind.Low ? "منخفضة" : "مرتفعة";
        var value = reading switch
        {
            { IsAboveRange: true } => "أعلى من نطاق الجهاز (أكثر من 600 mg/dL)",
            { IsBelowRange: true } => "أقل من نطاق الجهاز (أقل من 20 mg/dL)",
            _ => $"{reading.Raw} {reading.Unit}",
        };
        return $"قراءة السكر {level}: {value} بتاريخ {date} ميلادي الساعة {time} بتوقيت الرياض. إذا كنت تشعر بتعب، تواصل مع طبيبك.";
    }
}
