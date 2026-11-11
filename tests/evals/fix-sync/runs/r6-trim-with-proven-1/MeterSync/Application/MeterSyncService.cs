using MeterSync.Domain;
using MeterSync.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MeterSync.Application;

/// <summary>Pulls new readings from the vendor for every linked meter.</summary>
public sealed class MeterSyncService(SyncDbContext db, MeterVendorClient vendor, ILogger<MeterSyncService> logger)
{
    /// <summary>Syncs every meter; one meter failing doesn't stop the others.</summary>
    /// <param name="ct">Cancellation.</param>
    /// <returns>The number of readings added.</returns>
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
                db.ChangeTracker.Clear();
                logger.LogError(ex, "Sync failed for meter {SerialNumber}", meter.SerialNumber);
            }
        }

        return added;
    }

    private async Task<int> SyncMeterAsync(Meter meter, CancellationToken ct)
    {
        var incoming = await vendor.ReadingsAsync(meter.SerialNumber, ct);
        try
        {
            return await AddNewAsync(meter, incoming, ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            // Another pod inserted some of these readings first: reload what is known now and try once more.
            db.ChangeTracker.Clear();
            return await AddNewAsync(meter, incoming, ct);
        }
    }

    private async Task<int> AddNewAsync(Meter meter, IReadOnlyList<VendorReading> incoming, CancellationToken ct)
    {
        var incomingIds = incoming.Select(r => r.Id).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToArray();
        var known = (await db.Readings
                .Where(r => incomingIds.Contains(r.VendorId))
                .Select(r => r.VendorId)
                .ToListAsync(ct))
            .ToHashSet();

        var added = 0;
        foreach (var r in incoming.Where(r => !known.Contains(r.Id)))
        {
            if (!Reading.TryFromMeter(meter.PatientId, meter.Id, r.Id, r.Value, r.Unit, r.TakenAt, out var reading))
            {
                // The vendor id only: a glucose value is health data and stays out of the logs.
                logger.LogWarning("Skipping reading {VendorId} from meter {SerialNumber}: not a reading we can interpret", r.Id, meter.SerialNumber);
                continue;
            }

            db.Readings.Add(reading);
            known.Add(r.Id);
            added++;
        }

        if (added > 0)
        {
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }

        return added;
    }
}
