using System.Globalization;
using MeterSync.Domain;

namespace MeterSync.Application;

/// <summary>The SMS a patient gets for a reading out of range. Patients are in Riyadh and read Arabic.</summary>
public static class AlertText
{
    // ar-SA's calendar depends on the machine's ICU (Hijri on some images) and its digits can be Arabic-Indic,
    // so the date is formatted with an explicit Gregorian calendar and Latin digits.
    private static readonly DateTimeFormatInfo GregorianLatin = CreateGregorianLatin();

    // Resolved once: the tz database can be missing from a slim container. Riyadh has no DST, so UTC+3 is exact.
    private static readonly TimeZoneInfo Riyadh =
        TimeZoneInfo.TryFindSystemTimeZoneById("Asia/Riyadh", out var zone)
            ? zone
            : TimeZoneInfo.CreateCustomTimeZone("Asia/Riyadh", TimeSpan.FromHours(3), "Riyadh", "Riyadh");

    public static string For(Reading reading) => For(reading.Alert, reading.Raw, reading.Unit, reading.TakenAt);

    public static string For(AlertKind kind, string raw, string unit, DateTimeOffset takenAt)
    {
        var when = TimeZoneInfo.ConvertTime(takenAt, Riyadh).ToString("dd/MM/yyyy HH:mm", GregorianLatin);
        var value = Reading.IsAboveRange(raw) ? "أعلى من 600 ملغ/دل"
            : Reading.IsBelowRange(raw) ? "أقل من 20 ملغ/دل"
            : $"{raw} {UnitInArabic(unit)}";
        var level = kind == AlertKind.Low ? "منخفضة" : "مرتفعة";
        return $"قراءة السكر {value} {level} ({when} بتوقيت الرياض). إذا كنت تشعر بتعب، تواصل مع طبيبك.";
    }

    private static string UnitInArabic(string unit) =>
        unit.Equals(Reading.MmolL, StringComparison.OrdinalIgnoreCase) ? "مليمول/لتر"
        : unit.Equals(Reading.MgDl, StringComparison.OrdinalIgnoreCase) ? "ملغ/دل"
        : unit;

    private static DateTimeFormatInfo CreateGregorianLatin()
    {
        var format = (DateTimeFormatInfo)CultureInfo.InvariantCulture.DateTimeFormat.Clone();
        format.Calendar = new GregorianCalendar();
        return DateTimeFormatInfo.ReadOnly(format);
    }
}
