using System.Globalization;
using MeterSync.Domain;

namespace MeterSync.Application;

/// <summary>The SMS a patient gets for a reading out of range. Patients are in Riyadh and read Arabic.</summary>
public static class AlertText
{
    /// <summary>The text for <paramref name="reading"/>, short enough for one Arabic SMS part (70 characters).</summary>
    /// <param name="reading">A reading with an alert.</param>
    /// <returns>The Arabic text.</returns>
    /// <exception cref="ArgumentException">The reading is in range.</exception>
    public static string For(Reading reading)
    {
        // Gregorian, Latin digits, Riyadh time, on every machine: the invariant culture needs no ICU, and an
        // ar-SA culture would write a Hijri date on some ICU builds and fail to load on an image without ICU.
        var when = TimeZoneInfo.ConvertTime(reading.TakenAt, RiyadhTime.Zone)
            .ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
        var shown = reading.Raw.Trim();

        if (shown.Equals("HI", StringComparison.OrdinalIgnoreCase))
        {
            return $"سكرك أعلى مما يقيسه الجهاز ({when}). اتصل بطبيبك الآن.";
        }

        if (shown.Equals("LO", StringComparison.OrdinalIgnoreCase))
        {
            return $"سكرك أقل مما يقيسه الجهاز ({when}). اتصل بطبيبك الآن.";
        }

        var level = reading.Alert switch
        {
            AlertKind.Low => "منخفض",
            AlertKind.High => "مرتفع",
            _ => throw new ArgumentException("Only a reading out of range has an alert text.", nameof(reading)),
        };
        return $"سكرك {level}: {shown} {reading.Unit} في {when}. إن تعبت فاتصل بطبيبك.";
    }
}
