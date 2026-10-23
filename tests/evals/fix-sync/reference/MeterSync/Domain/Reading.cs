using System.Globalization;

namespace MeterSync.Domain;

public enum AlertKind
{
    None,
    Low,
    High,
}

public sealed class Reading
{
    // The care team's thresholds, in mg/dL.
    public const decimal LowBelow = 70m;
    public const decimal HighAbove = 250m;

    private Reading()
    {
    }

    public int Id { get; private set; }

    public int PatientId { get; private set; }

    public int MeterId { get; private set; }

    public string VendorId { get; private set; } = "";

    /// <summary>The value as the meter showed it.</summary>
    public string Raw { get; private set; } = "";

    public string Unit { get; private set; } = "";

    /// <summary>Null for HI and LO: the meter's range was passed, so the value isn't known.</summary>
    public decimal? ValueMgDl { get; private set; }

    public DateTimeOffset TakenAt { get; private set; }

    public AlertKind Alert { get; private set; }

    public DateTimeOffset? AlertSentAt { get; private set; }

    /// <summary>A pod sending this alert holds it until then; another pod skips it.</summary>
    public DateTimeOffset? AlertClaimedUntil { get; private set; }

    /// <summary>A reading as the meter showed it: a number in mg/dL or mmol/L, or HI or LO beyond the meter's range.
    /// Null when the value or unit isn't one of those.</summary>
    public static Reading? FromMeter(int patientId, int meterId, string vendorId, string raw, string unit, DateTimeOffset takenAt)
    {
        decimal? mgDl;
        AlertKind alert;
        if (string.Equals(raw, "LO", StringComparison.OrdinalIgnoreCase))
        {
            (mgDl, alert) = (null, AlertKind.Low);
        }
        else if (string.Equals(raw, "HI", StringComparison.OrdinalIgnoreCase))
        {
            (mgDl, alert) = (null, AlertKind.High);
        }
        else if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
                 && ToMgDl(value, unit) is { } converted)
        {
            mgDl = converted;
            alert = converted < LowBelow ? AlertKind.Low : converted > HighAbove ? AlertKind.High : AlertKind.None;
        }
        else
        {
            return null;
        }

        return new Reading
        {
            PatientId = patientId,
            MeterId = meterId,
            VendorId = vendorId,
            Raw = raw,
            Unit = unit,
            ValueMgDl = mgDl,
            TakenAt = takenAt,
            Alert = alert,
        };
    }

    private static decimal? ToMgDl(decimal value, string unit) => unit switch
    {
        "mg/dL" => value,
        "mmol/L" => Math.Round(value * 18m, 1),
        _ => null,
    };

    public void MarkAlertSent(DateTimeOffset at) => AlertSentAt = at;
}
