using System.Globalization;
using MeterSync.Domain;

namespace MeterSync.Application;

/// <summary>The SMS a patient gets for a reading out of range. Patients are in Riyadh and read Arabic.</summary>
public static class AlertText
{
    /// <summary>Builds the alert text, with the time in Riyadh on the Gregorian calendar.</summary>
    /// <param name="reading">An out-of-range reading.</param>
    /// <returns>The Arabic SMS text.</returns>
    public static string For(Reading reading) => For(reading, ArabicCulture.Gregorian);

    internal static string For(Reading reading, CultureInfo? arabic)
    {
        var local = TimeZoneInfo.ConvertTime(reading.TakenAt, RiyadhTime.Zone);
        var when = arabic is null
            ? local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
            : local.ToString("dd/MM/yyyy HH:mm", arabic);
        var value = reading switch
        {
            { IsAboveMeterRange: true } => string.Create(CultureInfo.InvariantCulture, $"أعلى من {Reading.AboveRangeMgDl:0} mg/dL"),
            { IsBelowMeterRange: true } => string.Create(CultureInfo.InvariantCulture, $"أقل من {Reading.BelowRangeMgDl:0} mg/dL"),
            _ => $"{reading.Raw} {reading.Unit}",
        };
        var level = reading.Alert == AlertKind.Low ? "منخفضة" : "مرتفعة";
        return $"قراءة السكر {value} {level} ({when}). إذا كنت تشعر بتعب، تواصل مع طبيبك.";
    }
}
