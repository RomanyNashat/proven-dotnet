using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace MeterSync.Domain;

public sealed class Reading
{
    // The care team's thresholds, in mg/dL.
    public const decimal LowBelow = 70m;
    public const decimal HighAbove = 250m;

    // VENDOR.md: HI is above the meter's range (over 600 mg/dL), LO below it (under 20 mg/dL). The bound is
    // stored so the reading sorts and alerts with the others; Raw keeps what the meter showed.
    public const decimal HiValueMgDl = 600m;
    public const decimal LoValueMgDl = 20m;
    public const decimal MgDlPerMmol = 18m;

    public const int MaxAlertAttempts = 5;

    // Column limits (SyncDbContext). A value outside them is rejected here, not by the database, so one bad
    // reading can't fail the insert for the others.
    public const int VendorIdMaxLength = 64;
    public const int RawMaxLength = 16;
    public const int UnitMaxLength = 8;
    private const decimal MaxStoredValue = 9999.9m;

    /// <summary>
    /// The vendor returns every reading a meter ever uploaded. Out-of-range readings taken more than this long
    /// before the meter was linked are its history: they are stored, but nobody is texted about them.
    /// Readings from the day before linking still alert. The window is a product decision; 24 hours until
    /// the care team sets one.
    /// </summary>
    public static readonly TimeSpan AlertHistoryWindow = TimeSpan.FromHours(24);

    private Reading()
    {
    }

    public int Id { get; private set; }

    public int PatientId { get; private set; }

    public int MeterId { get; private set; }

    public string VendorId { get; private set; } = "";

    /// <summary>The value as the meter showed it: a number, HI or LO.</summary>
    public string Raw { get; private set; } = "";

    /// <summary>The unit the meter showed, mg/dL or mmol/L.</summary>
    public string Unit { get; private set; } = "";

    public decimal ValueMgDl { get; private set; }

    public DateTimeOffset TakenAt { get; private set; }

    public AlertKind Alert { get; private set; }

    public AlertDelivery AlertDelivery { get; private set; }

    public int AlertAttempts { get; private set; }

    /// <summary>The sweeper that claimed this alert holds it until then; another pod may take it after.</summary>
    public DateTimeOffset? AlertClaimedUntil { get; private set; }

    public DateTimeOffset? AlertSentAt { get; private set; }

    public DateTimeOffset? AlertFailedAt { get; private set; }

    /// <summary>Reads one vendor reading for <paramref name="meter"/>, converting it to mg/dL.</summary>
    /// <param name="meter">The meter it came from.</param>
    /// <param name="vendorId">The vendor's id for the reading.</param>
    /// <param name="raw">The value as the meter showed it.</param>
    /// <param name="unit">mg/dL or mmol/L.</param>
    /// <param name="takenAt">When it was taken.</param>
    /// <param name="reading">The reading, when it was accepted.</param>
    /// <param name="rejection">Why it wasn't, without the value itself (it's health data and goes to logs).</param>
    /// <returns>Whether the reading was accepted.</returns>
    public static bool TryFromMeter(
        Meter meter, string? vendorId, string? raw, string? unit, DateTimeOffset takenAt,
        [NotNullWhen(true)] out Reading? reading, [NotNullWhen(false)] out string? rejection)
    {
        reading = null;
        rejection = Validate(vendorId, raw, unit, takenAt, out var valueMgDl);
        if (rejection is not null)
        {
            return false;
        }

        var takenAtUtc = takenAt.ToUniversalTime();
        var alert = valueMgDl < LowBelow ? AlertKind.Low : valueMgDl > HighAbove ? AlertKind.High : AlertKind.None;
        var isHistory = takenAtUtc < meter.LinkedAt - AlertHistoryWindow;
        reading = new Reading
        {
            PatientId = meter.PatientId,
            MeterId = meter.Id,
            VendorId = vendorId!,
            Raw = raw!,
            Unit = unit!,
            ValueMgDl = valueMgDl,
            TakenAt = takenAtUtc,
            Alert = alert,
            AlertDelivery = alert == AlertKind.None || isHistory ? AlertDelivery.NotNeeded : AlertDelivery.Pending,
        };
        return true;
    }

    public void MarkAlertSent(DateTimeOffset at)
    {
        AlertSentAt = at;
        AlertDelivery = AlertDelivery.Sent;
        AlertClaimedUntil = null;
    }

    /// <summary>
    /// A failed send. The claim is kept, so the next try waits for it to lapse (a backoff of one lease);
    /// after <see cref="MaxAlertAttempts"/> the alert is moved aside.
    /// </summary>
    public void RecordAlertFailure(DateTimeOffset at)
    {
        AlertAttempts++;
        if (AlertAttempts >= MaxAlertAttempts)
        {
            GiveUpAlert(at);
        }
    }

    /// <summary>The alert can't be sent at all (no phone number on file).</summary>
    public void GiveUpAlert(DateTimeOffset at)
    {
        AlertDelivery = AlertDelivery.Failed;
        AlertFailedAt = at;
        AlertClaimedUntil = null;
    }

    private static string? Validate(string? vendorId, string? raw, string? unit, DateTimeOffset takenAt, out decimal valueMgDl)
    {
        valueMgDl = 0;
        if (string.IsNullOrWhiteSpace(vendorId) || vendorId.Length > VendorIdMaxLength)
        {
            return $"the vendor id is missing or longer than {VendorIdMaxLength} characters";
        }

        if (takenAt == default)
        {
            return "the reading has no time";
        }

        if (string.IsNullOrWhiteSpace(raw) || raw.Length > RawMaxLength)
        {
            return $"the value is missing or longer than {RawMaxLength} characters";
        }

        decimal? mgDlPerUnit =
            string.Equals(unit, "mg/dL", StringComparison.OrdinalIgnoreCase) ? 1m
            : string.Equals(unit, "mmol/L", StringComparison.OrdinalIgnoreCase) ? MgDlPerMmol
            : null;
        if (mgDlPerUnit is null)
        {
            return "the unit is neither mg/dL nor mmol/L";
        }

        var shown = raw.Trim();
        if (shown.Equals("HI", StringComparison.OrdinalIgnoreCase))
        {
            valueMgDl = HiValueMgDl;
            return null;
        }

        if (shown.Equals("LO", StringComparison.OrdinalIgnoreCase))
        {
            valueMgDl = LoValueMgDl;
            return null;
        }

        if (!decimal.TryParse(shown, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
        {
            return "the value is not a number, HI or LO";
        }

        valueMgDl = Math.Round(value * mgDlPerUnit.Value, 1, MidpointRounding.AwayFromZero);
        return valueMgDl is > 0 and <= MaxStoredValue ? null : "the value is outside any plausible range";
    }
}
