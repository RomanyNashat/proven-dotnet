using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using MeterSync.Domain;
using MeterSync.Infrastructure;

namespace MeterSync.Application;

/// <summary>A vendor reading checked and converted to mg/dL, ready to store.</summary>
public sealed record ParsedVendorReading(string VendorId, string Raw, string Unit, decimal ValueMgDl, DateTimeOffset TakenAt)
{
    public Reading ToReading(int patientId, int meterId) =>
        Reading.FromMeter(patientId, meterId, VendorId, Raw, Unit, ValueMgDl, TakenAt);
}

/// <summary>Turns what the vendor sends (VENDOR.md) into a value the service can store and classify.</summary>
public static class VendorReadingParser
{
    public const string MgDl = "mg/dL";
    public const string MmolL = "mmol/L";

    // The meter shows HI above 600 mg/dL and LO below 20; storing the limit keeps them classifiable.
    public const decimal HiMgDl = 600m;
    public const decimal LoMgDl = 20m;

    private const decimal MgDlPerMmolL = 18m;

    // ValueMgDl is numeric(6,1); anything at or above 1000 is not a real reading.
    private const decimal MaxMgDlExclusive = 1000m;
    private const int MaxVendorIdLength = 64;
    private const int MaxRawLength = 16;
    private const int MaxUnitLength = 8;

    /// <summary>Checks and converts one vendor reading.</summary>
    /// <param name="reading">The reading as the vendor sent it.</param>
    /// <param name="parsed">The converted reading, when valid.</param>
    /// <param name="rejectReason">Why it was rejected. Never contains the value: glucose is health data.</param>
    /// <returns>Whether the reading can be stored.</returns>
    public static bool TryParse(
        VendorReading reading,
        [NotNullWhen(true)] out ParsedVendorReading? parsed,
        [NotNullWhen(false)] out string? rejectReason)
    {
        parsed = null;
        rejectReason = Validate(reading);
        if (rejectReason is not null)
        {
            return false;
        }

        var raw = reading.Value.Trim();
        var unit = string.Equals(reading.Unit.Trim(), MmolL, StringComparison.OrdinalIgnoreCase) ? MmolL : MgDl;
        decimal mgDl;
        if (string.Equals(raw, "HI", StringComparison.OrdinalIgnoreCase))
        {
            raw = "HI";
            mgDl = HiMgDl;
        }
        else if (string.Equals(raw, "LO", StringComparison.OrdinalIgnoreCase))
        {
            raw = "LO";
            mgDl = LoMgDl;
        }
        else if (decimal.TryParse(raw, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
        {
            mgDl = Math.Round(unit == MmolL ? value * MgDlPerMmolL : value, 1, MidpointRounding.AwayFromZero);
        }
        else
        {
            rejectReason = "value is not a number, HI or LO";
            return false;
        }

        if (mgDl is < 0 or >= MaxMgDlExclusive)
        {
            rejectReason = "value is outside the storable range";
            return false;
        }

        parsed = new ParsedVendorReading(reading.Id, raw, unit, mgDl, reading.TakenAt.ToUniversalTime());
        return true;
    }

    // The JSON can leave any field null despite the record's non-nullable types.
    private static string? Validate(VendorReading reading) => reading switch
    {
        { Id: null } or { Id.Length: 0 } => "vendor id is missing",
        { Id.Length: > MaxVendorIdLength } => "vendor id is too long",
        { Value: null } => "value is missing",
        { Value.Length: > MaxRawLength } => "value is too long",
        { Unit: null } or { Unit.Length: > MaxUnitLength } => "unit is missing or too long",
        _ when !IsKnownUnit(reading.Unit.Trim()) => "unit is not mg/dL or mmol/L",
        _ => null,
    };

    private static bool IsKnownUnit(string unit) =>
        string.Equals(unit, MgDl, StringComparison.OrdinalIgnoreCase)
        || string.Equals(unit, MmolL, StringComparison.OrdinalIgnoreCase);
}
