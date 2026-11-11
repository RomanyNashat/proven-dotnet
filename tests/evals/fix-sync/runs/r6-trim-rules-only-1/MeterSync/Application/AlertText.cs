using System.Globalization;
using MeterSync.Domain;

namespace MeterSync.Application;

/// <summary>The SMS a patient gets for a reading out of range. Patients are in Riyadh and read Arabic.</summary>
public static class AlertText
{
    // Riyadh is UTC+3 all year. Looked up once; ICU maps the IANA id on Linux and Windows alike.
    private static readonly TimeZoneInfo Riyadh = TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh");

    /// <summary>Builds the Arabic alert text for a reading.</summary>
    /// <param name="reading">A reading with a low or high alert.</param>
    /// <returns>The SMS text.</returns>
    public static string For(Reading reading)
    {
        // Never format with ar-SA: its calendar is Hijri (Umm al-Qura) or Gregorian depending on the machine's
        // ICU, and its digits can change. The date is Gregorian and says so (م), the time says it is Riyadh's.
        var local = TimeZoneInfo.ConvertTime(reading.TakenAt, Riyadh);
        var date = local.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        var clock = local.ToString("HH:mm", CultureInfo.InvariantCulture);
        var level = reading.Alert == AlertKind.Low ? "منخفضة" : "مرتفعة";
        return $"قراءة السكر {Value(reading)} {level} ({date} م، الساعة {clock} بتوقيت الرياض). إذا كنت تشعر بتعب، تواصل مع طبيبك.";
    }

    // HI and LO are what the meter shows off its scale (VENDOR.md); "HI mg/dL" means nothing to a patient.
    private static string Value(Reading reading) => reading.Raw.ToUpperInvariant() switch
    {
        "HI" => "أعلى من 600 mg/dL",
        "LO" => "أقل من 20 mg/dL",
        _ => $"{reading.Raw} {reading.Unit}",
    };
}
