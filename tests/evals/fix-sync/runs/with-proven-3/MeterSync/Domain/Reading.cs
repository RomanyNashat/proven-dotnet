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

    // What the meter shows past its range (VENDOR.md). The bound is stored as the value; Raw keeps "HI"/"LO".
    public const decimal MeterHighBound = 600m;
    public const decimal MeterLowBound = 20m;

    public const int MaxAlertAttempts = 5;

    // Column sizes, checked here so one odd vendor value is skipped instead of failing the insert.
    public const int VendorIdMaxLength = 64;
    public const int RawMaxLength = 16;
    public const int UnitMaxLength = 8;
    public const int LastErrorMaxLength = 200;

    private const decimal MgDlPerMmolL = 18m;

    // numeric(6,1) holds up to 99999.9.
    private const decimal LargestStorable = 99999.9m;

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

    public decimal ValueMgDl { get; private set; }

    public DateTimeOffset TakenAt { get; private set; }

    public AlertKind Alert { get; private set; }

    public AlertDelivery AlertDelivery { get; private set; }

    public int AlertAttempts { get; private set; }

    /// <summary>A sweeper holds the alert until then; after a failure, the next attempt waits until then.</summary>
    public DateTimeOffset? AlertClaimedUntil { get; private set; }

    public string? AlertLastError { get; private set; }

    public DateTimeOffset? AlertSentAt { get; private set; }

    /// <summary>
    /// A reading as the vendor sent it, converted to mg/dL and classified. Null when it can't be read: an
    /// unknown unit, a value that isn't a number, HI or LO, or a field longer than its column.
    /// </summary>
    /// <param name="alertsFrom">
    /// When the meter was linked. The vendor returns the meter's whole history; only readings taken from
    /// this moment on get an SMS.
    /// </param>
    public static Reading? FromMeter(int patientId, int meterId, string vendorId, string raw, string unit, DateTimeOffset takenAt, DateTimeOffset alertsFrom)
    {
        if (vendorId.Length is 0 or > VendorIdMaxLength || raw.Length > RawMaxLength || unit.Length > UnitMaxLength)
        {
            return null;
        }

        if (ToMgDl(raw, unit) is not { } value)
        {
            return null;
        }

        var alert = value < LowBelow ? AlertKind.Low : value > HighAbove ? AlertKind.High : AlertKind.None;
        return new Reading
        {
            PatientId = patientId,
            MeterId = meterId,
            VendorId = vendorId,
            Raw = raw,
            Unit = unit,
            ValueMgDl = value,
            TakenAt = takenAt.ToUniversalTime(),
            Alert = alert,
            AlertDelivery = alert != AlertKind.None && takenAt >= alertsFrom ? AlertDelivery.Pending : AlertDelivery.NotNeeded,
        };
    }

    public void MarkAlertSent(DateTimeOffset at)
    {
        AlertDelivery = AlertDelivery.Sent;
        AlertSentAt = at;
        AlertClaimedUntil = null;
    }

    /// <summary>Counts a failed attempt: the alert is tried again at <paramref name="retryAt"/>, or parked after the last attempt.</summary>
    public void RecordAlertFailure(string error, DateTimeOffset retryAt)
    {
        AlertAttempts++;
        AlertLastError = error.Length > LastErrorMaxLength ? error[..LastErrorMaxLength] : error;
        if (AlertAttempts >= MaxAlertAttempts)
        {
            AlertDelivery = AlertDelivery.Failed;
            AlertClaimedUntil = null;
            return;
        }

        AlertClaimedUntil = retryAt;
    }

    private static decimal? ToMgDl(string raw, string unit)
    {
        // Units are compared exactly: the vendor sends "mg/dL" or "mmol/L", and anything else is a reading we
        // can't interpret safely.
        decimal? perUnit = unit switch
        {
            "mg/dL" => 1m,
            "mmol/L" => MgDlPerMmolL,
            _ => null,
        };
        if (perUnit is not { } factor)
        {
            return null;
        }

        switch (raw.Trim().ToUpperInvariant())
        {
            case "HI":
                return MeterHighBound;
            case "LO":
                return MeterLowBound;
        }

        if (!decimal.TryParse(raw, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, CultureInfo.InvariantCulture, out var shown))
        {
            return null;
        }

        var value = Math.Round(shown * factor, 1, MidpointRounding.AwayFromZero);
        return value <= LargestStorable ? value : null;
    }
}
