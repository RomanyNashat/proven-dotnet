using System.Globalization;

namespace MeterSync.Domain;

public enum AlertKind
{
    None,
    Low,
    High,
}

/// <summary>A reading accepted from the vendor, or the reason it was not (never containing the value).</summary>
public sealed record ReadingParseResult(Reading? Reading, string? Rejection);

public sealed class Reading
{
    // The care team's thresholds, in mg/dL.
    public const decimal LowBelow = 70m;
    public const decimal HighAbove = 250m;

    /// <summary>What the meter shows above its range (over 600 mg/dL) and below it (under 20 mg/dL).</summary>
    public const string AboveRange = "HI";
    public const string BelowRange = "LO";

    public const string MgDl = "mg/dL";
    public const string MmolL = "mmol/L";

    public const int VendorIdMaxLength = 64;
    public const int RawMaxLength = 16;
    public const int UnitMaxLength = 8;

    /// <summary>A numeric(6,1) column holds up to 99999.9.</summary>
    public const decimal MaxMgDl = 99999.9m;

    /// <summary>After this many failed sends the alert is parked and no longer retried.</summary>
    public const int MaxAlertAttempts = 5;

    private const decimal MgDlPerMmolL = 18m;

    private Reading()
    {
    }

    /// <summary>An alert about an older reading is stale: it is stored but never texted.</summary>
    public static TimeSpan AlertWindow { get; } = TimeSpan.FromHours(12);

    public int Id { get; private set; }

    public int PatientId { get; private set; }

    public int MeterId { get; private set; }

    public string VendorId { get; private set; } = "";

    /// <summary>The value as the meter showed it.</summary>
    public string Raw { get; private set; } = "";

    public string Unit { get; private set; } = "";

    /// <summary>The value in mg/dL; null when the meter showed HI or LO.</summary>
    public decimal? ValueMgDl { get; private set; }

    public DateTimeOffset TakenAt { get; private set; }

    public AlertKind Alert { get; private set; }

    public DateTimeOffset? AlertSentAt { get; private set; }

    /// <summary>The lease an instance holds while it sends the alert.</summary>
    public DateTimeOffset? AlertClaimedUntil { get; private set; }

    public int AlertAttempts { get; private set; }

    public static bool IsAboveRange(string raw) => string.Equals(raw, AboveRange, StringComparison.OrdinalIgnoreCase);

    public static bool IsBelowRange(string raw) => string.Equals(raw, BelowRange, StringComparison.OrdinalIgnoreCase);

    /// <summary>Parses and classifies a reading as the vendor sent it (see VENDOR.md).</summary>
    public static ReadingParseResult FromMeter(
        int patientId, int meterId, string? vendorId, string? raw, string? unit, DateTimeOffset takenAt)
    {
        if (string.IsNullOrWhiteSpace(vendorId) || vendorId.Length > VendorIdMaxLength)
        {
            return Reject($"the vendor id is missing or longer than {VendorIdMaxLength} characters");
        }

        if (string.IsNullOrWhiteSpace(raw) || raw.Length > RawMaxLength)
        {
            return Reject($"the value is missing or longer than {RawMaxLength} characters");
        }

        if (string.IsNullOrWhiteSpace(unit) || unit.Length > UnitMaxLength)
        {
            return Reject($"the unit is missing or longer than {UnitMaxLength} characters");
        }

        decimal? factor = unit.Equals(MgDl, StringComparison.OrdinalIgnoreCase) ? 1m
            : unit.Equals(MmolL, StringComparison.OrdinalIgnoreCase) ? MgDlPerMmolL
            : null;
        if (factor is null)
        {
            return Reject("the unit is unknown");
        }

        var reading = new Reading
        {
            PatientId = patientId,
            MeterId = meterId,
            VendorId = vendorId,
            Raw = raw,
            Unit = unit,
            TakenAt = takenAt.ToUniversalTime(),
        };

        if (IsAboveRange(raw) || IsBelowRange(raw))
        {
            reading.Alert = IsAboveRange(raw) ? AlertKind.High : AlertKind.Low;
            return new(reading, null);
        }

        if (!decimal.TryParse(raw, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
        {
            return Reject("the value is not a number");
        }

        var mgDl = Math.Round(value * factor.Value, 1, MidpointRounding.AwayFromZero);
        if (mgDl > MaxMgDl)
        {
            return Reject("the value is out of range");
        }

        reading.ValueMgDl = mgDl;
        reading.Alert = mgDl < LowBelow ? AlertKind.Low : mgDl > HighAbove ? AlertKind.High : AlertKind.None;
        return new(reading, null);
    }

    private static ReadingParseResult Reject(string reason) => new(null, reason);
}
