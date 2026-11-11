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

    public const string MgDl = "mg/dL";
    public const string MmolL = "mmol/L";

    // What the meter shows when the value is outside its measuring range (see VENDOR.md).
    public const string AboveRange = "HI";
    public const string BelowRange = "LO";

    public const int VendorIdMaxLength = 64;
    public const int RawMaxLength = 16;

    private const decimal MgDlPerMmolL = 18m;

    // numeric(6,1) holds up to 99999.9; anything near that is a corrupt value, not a glucose reading.
    private const decimal MaxPlausibleMgDl = 2000m;

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

    /// <summary>The value in mg/dL, or <c>null</c> when the meter showed HI or LO.</summary>
    public decimal? ValueMgDl { get; private set; }

    public DateTimeOffset TakenAt { get; private set; }

    public AlertKind Alert { get; private set; }

    public DateTimeOffset? AlertSentAt { get; private set; }

    /// <summary>Set while one instance is sending the SMS, so another pod doesn't send it too.</summary>
    public DateTimeOffset? AlertClaimedUntil { get; private set; }

    public bool IsAboveRange => string.Equals(Raw, AboveRange, StringComparison.Ordinal);

    public bool IsBelowRange => string.Equals(Raw, BelowRange, StringComparison.Ordinal);

    /// <summary>Builds a reading from what the vendor reported.</summary>
    /// <returns><c>false</c> when the value or the unit can't be read safely; the caller should skip it.</returns>
    public static bool TryFromMeter(
        int patientId, int meterId, string vendorId, string? raw, string? unit, DateTimeOffset takenAt, out Reading? reading)
    {
        reading = null;
        var normalizedUnit = NormalizeUnit(unit);
        var value = raw?.Trim() ?? "";
        if (normalizedUnit is null || string.IsNullOrWhiteSpace(vendorId) || vendorId.Length > VendorIdMaxLength
            || value.Length is 0 or > RawMaxLength)
        {
            return false;
        }

        decimal? mgDl;
        AlertKind alert;
        if (value.Equals(AboveRange, StringComparison.OrdinalIgnoreCase))
        {
            (value, mgDl, alert) = (AboveRange, null, AlertKind.High);
        }
        else if (value.Equals(BelowRange, StringComparison.OrdinalIgnoreCase))
        {
            (value, mgDl, alert) = (BelowRange, null, AlertKind.Low);
        }
        else
        {
            // AllowDecimalPoint only: NumberStyles.Number would read "5,6" as 56.
            if (!decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed))
            {
                return false;
            }

            var converted = normalizedUnit == MmolL ? parsed * MgDlPerMmolL : parsed;
            converted = Math.Round(converted, 1, MidpointRounding.AwayFromZero);
            if (converted <= 0 || converted > MaxPlausibleMgDl)
            {
                return false;
            }

            mgDl = converted;
            alert = converted < LowBelow ? AlertKind.Low : converted > HighAbove ? AlertKind.High : AlertKind.None;
        }

        reading = new Reading
        {
            PatientId = patientId,
            MeterId = meterId,
            VendorId = vendorId,
            Raw = value,
            Unit = normalizedUnit,
            ValueMgDl = mgDl,
            TakenAt = takenAt.ToUniversalTime(),
            Alert = alert,
        };
        return true;
    }

    private static string? NormalizeUnit(string? unit) => unit?.Trim() switch
    {
        { } u when u.Equals(MgDl, StringComparison.OrdinalIgnoreCase) => MgDl,
        { } u when u.Equals(MmolL, StringComparison.OrdinalIgnoreCase) => MmolL,
        _ => null,
    };
}
