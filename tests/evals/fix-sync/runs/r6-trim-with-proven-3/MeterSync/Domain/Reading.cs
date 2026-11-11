using System.Diagnostics.CodeAnalysis;
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

    // Column limits; a longer vendor value would fail the insert for the whole meter.
    public const int VendorIdMaxLength = 64;
    public const int RawMaxLength = 16;
    public const int UnitMaxLength = 8;

    private const string MgDl = "mg/dL";
    private const string MmolL = "mmol/L";
    private const decimal MgDlPerMmolL = 18m;

    // numeric(6,1) holds at most 99999.9.
    private const decimal MaxStorableMgDl = 99999.9m;

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

    /// <summary>The value in mg/dL; null when the meter showed HI or LO (outside its range).</summary>
    public decimal? ValueMgDl { get; private set; }

    public DateTimeOffset TakenAt { get; private set; }

    public AlertKind Alert { get; private set; }

    public DateTimeOffset? AlertSentAt { get; private set; }

    /// <summary>When a sweeper last claimed this alert for sending (a lease, so two pods never both text it).</summary>
    public DateTimeOffset? AlertClaimedAt { get; private set; }

    public int AlertAttempts { get; private set; }

    /// <summary>Builds a reading from what the vendor reports for <paramref name="meter"/>.</summary>
    /// <param name="meter">The linked meter the reading came from.</param>
    /// <param name="vendorId">The vendor's id for the reading.</param>
    /// <param name="raw">The value as the meter's screen showed it: a number, <c>HI</c> or <c>LO</c>.</param>
    /// <param name="unit"><c>mg/dL</c> or <c>mmol/L</c>.</param>
    /// <param name="takenAt">When the reading was taken.</param>
    /// <param name="reading">The reading, when it is valid.</param>
    /// <param name="rejection">Why the reading was rejected. Never contains the value (health data).</param>
    /// <returns>Whether the reading is valid.</returns>
    public static bool TryFromMeter(
        Meter meter,
        string? vendorId,
        string? raw,
        string? unit,
        DateTimeOffset takenAt,
        [NotNullWhen(true)] out Reading? reading,
        [NotNullWhen(false)] out string? rejection)
    {
        reading = null;
        rejection = Reject(vendorId, raw, unit);
        if (rejection is not null)
        {
            return false;
        }

        if (!TryClassify(raw!, unit!, out var valueMgDl, out var alert))
        {
            rejection = "the value is not a reading in the stated unit";
            return false;
        }

        reading = new Reading
        {
            PatientId = meter.PatientId,
            MeterId = meter.Id,
            VendorId = vendorId!,
            Raw = raw!,
            Unit = unit!,
            ValueMgDl = valueMgDl,
            TakenAt = takenAt.ToUniversalTime(),

            // The vendor returns the meter's whole history: readings from before the link are kept
            // but never texted, or linking a meter would send the patient months of old alerts.
            Alert = takenAt < meter.LinkedAt ? AlertKind.None : alert,
        };
        return true;
    }

    private static string? Reject(string? vendorId, string? raw, string? unit) => (vendorId, raw, unit) switch
    {
        _ when string.IsNullOrWhiteSpace(vendorId) || vendorId.Length > VendorIdMaxLength => "the vendor id is missing or too long",
        _ when string.IsNullOrWhiteSpace(raw) || raw.Length > RawMaxLength => "the value is missing or too long",
        _ when unit is not (MgDl or MmolL) => "the unit is not mg/dL or mmol/L",
        _ => null,
    };

    private static bool TryClassify(string raw, string unit, out decimal? valueMgDl, out AlertKind alert)
    {
        valueMgDl = null;
        if (string.Equals(raw, "HI", StringComparison.OrdinalIgnoreCase))
        {
            alert = AlertKind.High;
            return true;
        }

        if (string.Equals(raw, "LO", StringComparison.OrdinalIgnoreCase))
        {
            alert = AlertKind.Low;
            return true;
        }

        alert = AlertKind.None;

        // AllowDecimalPoint only: "1,2" must not become 12, and a sign or exponent is not a reading.
        if (!decimal.TryParse(raw, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        var mgDl = Math.Round(unit == MmolL ? value * MgDlPerMmolL : value, 1, MidpointRounding.AwayFromZero);
        if (mgDl > MaxStorableMgDl)
        {
            return false;
        }

        valueMgDl = mgDl;
        alert = mgDl < LowBelow ? AlertKind.Low : mgDl > HighAbove ? AlertKind.High : AlertKind.None;
        return true;
    }
}
