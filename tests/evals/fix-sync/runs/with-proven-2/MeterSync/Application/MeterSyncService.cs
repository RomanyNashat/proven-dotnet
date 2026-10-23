using MeterSync.Domain;
using MeterSync.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MeterSync.Application;

/// <summary>Pulls new readings from the vendor for every linked meter.</summary>
public sealed class MeterSyncService(SyncDbContext db, MeterVendorClient vendor, ILogger<MeterSyncService> logger)
{
    /// <summary>One pass over every meter. A meter that fails is logged and skipped; the others still sync.</summary>
    /// <param name="ct">Cancellation.</param>
    /// <returns>How many readings were added.</returns>
    public async Task<int> SyncOnceAsync(CancellationToken ct = default)
    {
        var meters = await db.Meters.AsNoTracking().OrderBy(m => m.Id).ToListAsync(ct);
        var added = 0;
        foreach (var meter in meters)
        {
            try
            {
                added += await SyncMeterAsync(meter, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // The type only: the message of a vendor or database error can carry the serial or a value.
                logger.LogWarning("Sync of meter {MeterId} failed with {ErrorType}", meter.Id, ex.GetType().Name);
            }
        }

        return added;
    }

    private async Task<int> SyncMeterAsync(Meter meter, CancellationToken ct)
    {
        var incoming = await vendor.ReadingsAsync(meter.SerialNumber, ct);
        var incomingIds = incoming.Select(r => r.Id).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        if (incomingIds.Count == 0)
        {
            return 0;
        }

        // The vendor returns the meter's whole history every time; only the ids not stored yet go further.
        var known = (await db.Readings.Where(r => incomingIds.Contains(r.VendorId)).Select(r => r.VendorId).ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);

        var fresh = new List<Reading>();
        foreach (var r in incoming)
        {
            if (r.Id is null || known.Contains(r.Id))
            {
                continue;
            }

            if (!Reading.TryFromMeter(meter, r.Id, r.Value, r.Unit, r.TakenAt, out var reading, out var rejection))
            {
                // The vendor id and the reason, never the value: a glucose reading is health data.
                logger.LogWarning("Skipping reading {VendorId} of meter {MeterId}: {Reason}", r.Id, meter.Id, rejection);
                continue;
            }

            known.Add(r.Id);   // a vendor id repeated within one response is inserted once
            fresh.Add(reading);
        }

        return fresh.Count == 0 ? 0 : await db.InsertNewAsync(fresh, ct);
    }
}
