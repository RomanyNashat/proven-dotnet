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
                var reading = Reading.FromMeter(meter.PatientId, meter.Id, r.Id, r.Value, r.Unit, r.TakenAt.ToUniversalTime());
                if (reading is null)
                {
                    logger.LogWarning("Skipping reading {VendorId}: {Value} {Unit} isn't a reading this service knows", r.Id, r.Value, r.Unit);
                    continue;
                }

                db.Readings.Add(reading);
                added++;
            }
        }

        await db.SaveChangesAsync(ct);
        return added;
    }
}
