using System.Globalization;
using MeterSync.Domain;

namespace MeterSync.Application;

/// <summary>The SMS a patient gets for a reading out of range. Patients are in Riyadh and read Arabic.</summary>
public static class AlertText
{
    // Riyadh is UTC+3 all year with no daylight saving. A fixed offset avoids depending on the container
    // having tzdata, where FindSystemTimeZoneById("Asia/Riyadh") throws.
    private static readonly TimeSpan Riyadh = TimeSpan.FromHours(3);

    /// <summary>Builds the Arabic alert text.</summary>
    /// <param name="reading">A reading whose alert is Low or High.</param>
    /// <returns>The SMS text.</returns>
    public static string For(Reading reading)
    {
        // Never format with ar-SA: its calendar is Hijri (Um Al-Qura) or Gregorian depending on the machine's
        // ICU. The date is Gregorian with Latin digits, and the text says so.
        var when = reading.TakenAt.ToOffset(Riyadh).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
        var level = reading.Alert == AlertKind.Low ? "منخفضة" : "مرتفعة";
        var value = reading.Raw.Trim().ToUpperInvariant() switch
        {
            "HI" => $"أعلى من {Glucose.MeterMax.ToString(CultureInfo.InvariantCulture)} {Glucose.MgDl}",
            "LO" => $"أقل من {Glucose.MeterMin.ToString(CultureInfo.InvariantCulture)} {Glucose.MgDl}",
            _ => $"{reading.Raw} {reading.Unit}",
        };
        return $"قراءة السكر {value} {level} ({when} ميلادي، بتوقيت الرياض). إذا كنت تشعر بتعب، تواصل مع طبيبك.";
    }
}
