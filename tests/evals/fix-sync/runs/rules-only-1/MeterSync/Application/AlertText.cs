using System.Globalization;
using MeterSync.Domain;

namespace MeterSync.Application;

/// <summary>The SMS a patient gets for a reading out of range. Patients are in Riyadh and read Arabic.</summary>
public static class AlertText
{
    // Riyadh is UTC+3 all year (no daylight saving). A fixed offset avoids needing tzdata in the image,
    // where FindSystemTimeZoneById("Asia/Riyadh") throws on a slim image without it.
    private static readonly TimeSpan RiyadhOffset = TimeSpan.FromHours(3);

    /// <summary>Builds the alert text.</summary>
    /// <param name="reading">An out-of-range reading.</param>
    /// <returns>The Arabic SMS text.</returns>
    public static string For(Reading reading)
    {
        // Formatted with the invariant culture: ar-SA's calendar depends on the machine's ICU (Hijri on some
        // images, so "dd/MM/yyyy" prints a Hijri date that looks Gregorian) and the culture can't be created
        // at all in globalization-invariant mode. The date is Gregorian and says so with "م".
        var local = reading.TakenAt.ToOffset(RiyadhOffset);
        var date = local.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        var clock = local.ToString("HH:mm", CultureInfo.InvariantCulture);
        var when = $"{date}م الساعة {clock} بتوقيت الرياض";

        var (value, level) = reading.Raw switch
        {
            GlucoseValue.High => ("أعلى من نطاق قياس الجهاز (HI)", "مرتفعة جدًا"),
            GlucoseValue.Low => ("أقل من نطاق قياس الجهاز (LO)", "منخفضة جدًا"),
            _ => ($"{reading.Raw} {reading.Unit}", reading.Alert == AlertKind.Low ? "منخفضة" : "مرتفعة"),
        };

        return $"قراءة السكر {value} {level} ({when}). إذا كنت تشعر بتعب، تواصل مع طبيبك.";
    }
}
