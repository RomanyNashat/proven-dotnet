using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq.Expressions;

namespace MeterSync.Domain;

public sealed class Reading
{
    // The care team's thresholds, in mg/dL.
    public const decimal LowBelow = 70m;
    public const decimal HighAbove = 250m;

    /// <summary>What the meter shows above its range (over 600 mg/dL, VENDOR.md).</summary>
    public const string AboveRange = "HI";

    /// <summary>What the meter shows below its range (under 20 mg/dL, VENDOR.md).</summary>
    public const string BelowRange = "LO";

    public const decimal AboveRangeMgDl = 600m;
    public const decimal BelowRangeMgDl = 20m;

    public const int VendorIdMaxLength = 64;
    public const int RawMaxLength = 16;
    public const int UnitMaxLength = 8;

    /// <summary>A text is sent at most this many times before the sweeper gives up on a reading.</summary>
    public const int MaxAlertAttempts = 3;

    private const string MgDl = "mg/dL";
    private const string MmolL = "mmol/L";
    private const decimal MgDlPerMmolL = 18m;

    // Largest value numeric(6,1) holds: anything bigger would fail the meter's save on every run.
    private const decimal MaxStorableMgDl = 99_999.9m;

    /// <summary>
    /// Only readings this recent are texted. The vendor returns a meter's whole history, so without it linking a
    /// meter, or catching up after an outage, would text every old out-of-range reading.
    /// </summary>
    public static readonly TimeSpan AlertWindow = TimeSpan.FromHours(24);

    /// <summary>How long one pod owns a reading's alert before another pod may retry it.</summary>
    public static readonly TimeSpan AlertLease = TimeSpan.FromMinutes(5);

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

    public DateTimeOffset? AlertSentAt { get; private set; }

    public DateTimeOffset? AlertClaimedUntil { get; private set; }

    public int AlertAttempts { get; private set; }

    public bool IsAboveMeterRange => Raw == AboveRange;

    public bool IsBelowMeterRange => Raw == BelowRange;

    /// <summary>Readings whose alert can be claimed at <paramref name="now"/>: not sent, not exhausted, no live lease.</summary>
    public static Expression<Func<Reading, bool>> ClaimableForAlert(DateTimeOffset now) =>
        r => r.AlertSentAt == null
             && r.AlertAttempts < MaxAlertAttempts
             && (r.AlertClaimedUntil == null || r.AlertClaimedUntil < now);

    /// <summary>Out-of-range readings taken within <see cref="AlertWindow"/> whose alert can be claimed.</summary>
    public static Expression<Func<Reading, bool>> DueForAlert(DateTimeOffset now)
    {
        var since = now - AlertWindow;
        return r => r.Alert != AlertKind.None
                    && r.TakenAt >= since
                    && r.AlertSentAt == null
                    && r.AlertAttempts < MaxAlertAttempts
                    && (r.AlertClaimedUntil == null || r.AlertClaimedUntil < now);
    }

    /// <summary>
    /// Interprets a reading as the vendor sends it. Returns false for anything that can't be read safely
    /// (unknown unit, a value that isn't a plain number, HI or LO, or a field longer than its column).
    /// </summary>
    public static bool TryFromMeter(
        int patientId, int meterId, string vendorId, string raw, string unit, DateTimeOffset takenAt, [NotNullWhen(true)] out Reading? reading)
    {
        reading = null;
        if (string.IsNullOrWhiteSpace(vendorId) || vendorId.Length > VendorIdMaxLength
            || string.IsNullOrEmpty(raw) || raw.Length > RawMaxLength
            || !TryInterpret(raw, unit, out var canonicalRaw, out var mgDl))
        {
            return false;
        }

        reading = new Reading
        {
            PatientId = patientId,
            MeterId = meterId,
            VendorId = vendorId,
            Raw = canonicalRaw,
            Unit = unit,
            ValueMgDl = mgDl,
            TakenAt = takenAt.ToUniversalTime(),
            Alert = Classify(mgDl),
        };
        return true;
    }

    /// <summary>Takes the alert for this pod until <paramref name="now"/> plus <see cref="AlertLease"/>.</summary>
    public void ClaimAlert(DateTimeOffset now)
    {
        AlertClaimedUntil = now + AlertLease;
        AlertAttempts++;
    }

    public void MarkAlertSent(DateTimeOffset at) => AlertSentAt = at;

    private static bool TryInterpret(string raw, string unit, out string canonicalRaw, out decimal mgDl)
    {
        canonicalRaw = raw;
        mgDl = 0;
        var mgPerUnit = unit switch
        {
            _ when string.Equals(unit, MgDl, StringComparison.OrdinalIgnoreCase) => 1m,
            _ when string.Equals(unit, MmolL, StringComparison.OrdinalIgnoreCase) => MgDlPerMmolL,
            _ => 0m,
        };
        if (mgPerUnit == 0m)
        {
            return false;
        }

        // HI and LO are the meter's range limits, whatever unit it is set to.
        if (string.Equals(raw, AboveRange, StringComparison.OrdinalIgnoreCase))
        {
            (canonicalRaw, mgDl) = (AboveRange, AboveRangeMgDl);
            return true;
        }

        if (string.Equals(raw, BelowRange, StringComparison.OrdinalIgnoreCase))
        {
            (canonicalRaw, mgDl) = (BelowRange, BelowRangeMgDl);
            return true;
        }

        // Plain digits and a decimal point only: no signs, exponents, spaces or thousands separators.
        if (!decimal.TryParse(raw, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        // Rounded to what the column stores, so the alert matches the stored value.
        mgDl = Math.Round(value * mgPerUnit, 1, MidpointRounding.AwayFromZero);
        return mgDl <= MaxStorableMgDl;
    }

    private static AlertKind Classify(decimal mgDl) => mgDl switch
    {
        < LowBelow => AlertKind.Low,
        > HighAbove => AlertKind.High,
        _ => AlertKind.None,
    };
}
