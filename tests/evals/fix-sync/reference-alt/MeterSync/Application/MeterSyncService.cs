using System.Globalization;
using MeterSync.Domain;
using MeterSync.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MeterSync.Application;

/// <summary>Pulls new readings from the vendor for every linked meter.</summary>
public sealed class MeterSyncService(SyncDbContext db, MeterVendorClient vendor, ILogger<MeterSyncService> logger)
{
    public async Task<int> SyncOnceAsync(CancellationToken ct = default)
    {
        var meters = await db.Meters.AsNoTracking().ToListAsync(ct);
        var added = 0;
        foreach (var meter in meters)
        {
            var readings = await vendor.ReadingsAsync(meter.SerialNumber, ct);
            var known = await db.Readings.Where(r => r.MeterId == meter.Id).Select(r => r.VendorId).ToListAsync(ct);
            foreach (var r in readings.Where(r => !known.Contains(r.Id)))
            {
                if (MgDl(r.Value, r.Unit) is not { } value)
                {
                    logger.LogWarning("Skipping reading {VendorId}: {Value} {Unit} can't be read", r.Id, r.Value, r.Unit);
                    continue;
                }

                db.Readings.Add(Reading.FromMeter(meter.PatientId, meter.Id, r.Id, r.Value, r.Unit, value, r.TakenAt.ToUniversalTime()));
                added++;
            }
        }

        await db.SaveChangesAsync(ct);
        return added;
    }

    // HI and LO are beyond the meter's range: past 600 and under 20 mg/dL, so they count as those limits.
    private static decimal? MgDl(string value, string unit) => value.ToUpperInvariant() switch
    {
        "HI" => 600m,
        "LO" => 20m,
        _ when decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) => unit switch
        {
            "mg/dL" => number,
            "mmol/L" => number * 18m,
            _ => null,
        },
        _ => null,
    };
}
