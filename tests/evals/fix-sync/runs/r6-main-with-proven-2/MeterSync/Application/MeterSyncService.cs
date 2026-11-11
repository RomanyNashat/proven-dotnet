using MeterSync.Domain;
using MeterSync.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MeterSync.Application;

/// <summary>Pulls new readings from the vendor for every linked meter.</summary>
public sealed class MeterSyncService(SyncDbContext db, MeterVendorClient vendor, ILogger<MeterSyncService> logger)
{
    /// <returns>The number of readings stored.</returns>
    public async Task<int> SyncOnceAsync(CancellationToken ct = default)
    {
        var meters = await db.Meters
            .OrderBy(m => m.Id)
            .Select(m => new { m.Id, m.PatientId, m.SerialNumber })
            .ToListAsync(ct);

        var added = 0;
        foreach (var meter in meters)
        {
            // One meter failing (vendor error, bad payload) must not stop the others from syncing.
            try
            {
                var readings = new List<Reading>();
                foreach (var r in await vendor.ReadingsAsync(meter.SerialNumber, ct))
                {
                    var parsed = Reading.FromMeter(meter.PatientId, meter.Id, r.Id, r.Value, r.Unit, r.TakenAt);
                    if (parsed.Reading is null)
                    {
                        logger.LogWarning("Skipping reading {VendorId} of meter {MeterId}: {Reason}", r.Id, meter.Id, parsed.Rejection);
                        continue;
                    }

                    readings.Add(parsed.Reading);
                }

                added += await db.InsertIgnoringDuplicatesAsync(readings, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError(ex, "Sync of meter {MeterId} failed", meter.Id);
            }
        }

        return added;
    }
}
