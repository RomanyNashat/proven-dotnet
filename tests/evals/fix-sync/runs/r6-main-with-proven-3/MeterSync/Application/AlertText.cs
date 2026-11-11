using System.Globalization;
using MeterSync.Domain;

namespace MeterSync.Application;

/// <summary>The SMS a patient gets for a reading out of range. Patients are in Riyadh and read Arabic.</summary>
public static class AlertText
{
    public static string For(Reading reading)
    {
        // "2 نوفمبر 2026 10:58": the month name shows the calendar, and the digits stay Latin.
        var when = TimeZoneInfo.ConvertTime(reading.TakenAt, RiyadhTime.Zone)
            .ToString("d MMMM yyyy HH:mm", ArabicCulture.ArabicGregorian);
        var value = reading switch
        {
            { IsAboveMeterRange: true } => string.Create(CultureInfo.InvariantCulture, $"أعلى من {Reading.HiMgDl:0} {Reading.MgDl}"),
            { IsBelowMeterRange: true } => string.Create(CultureInfo.InvariantCulture, $"أقل من {Reading.LoMgDl:0} {Reading.MgDl}"),
            _ => $"{reading.Raw} {reading.Unit}",
        };
        var level = reading.Alert == AlertKind.Low ? "منخفضة" : "مرتفعة";
        return $"قراءة السكر {value} {level} ({when}). إذا كنت تشعر بتعب، تواصل مع طبيبك.";
    }
}
