using MeterSync.Domain;
using MeterSync.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MeterSync.Application;

/// <summary>Pulls new readings from the vendor for every linked meter.</summary>
public sealed class MeterSyncService(SyncDbContext db, MeterVendorClient vendor, ILogger<MeterSyncService> logger)
{
    /// <summary>Imports the readings not stored yet. Safe to run on several pods at once.</summary>
    /// <param name="ct">Cancellation.</param>
    /// <returns>The number of readings this call stored.</returns>
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
                // One meter failing (vendor 404/5xx, bad JSON) must not stop the others from syncing.
                logger.LogError(ex, "Syncing meter {MeterId} failed", meter.Id);
            }
            finally
            {
                db.ChangeTracker.Clear();
            }
        }

        return added;
    }

    private async Task<int> SyncMeterAsync(Meter meter, CancellationToken ct)
    {
        var fromVendor = (await vendor.ReadingsAsync(meter.SerialNumber, ct))
            .Where(r => !string.IsNullOrWhiteSpace(r.Id))
            .DistinctBy(r => r.Id, StringComparer.Ordinal)
            .ToList();
        if (fromVendor.Count == 0)
        {
            return 0;
        }

        // The vendor returns the meter's whole history, so only look up the IDs in this response.
        var ids = fromVendor.Select(r => r.Id).ToList();
        var known = (await db.Readings.Where(r => ids.Contains(r.VendorId)).Select(r => r.VendorId).ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);

        var added = 0;
        foreach (var r in fromVendor.Where(r => !known.Contains(r.Id)))
        {
            if (!Reading.TryFromMeter(meter.PatientId, meter.Id, r.Id, r.Value, r.Unit, r.TakenAt, out var reading))
            {
                logger.LogWarning("Skipping reading {VendorId} of meter {MeterId}: can't read {Value} {Unit}", r.Id, meter.Id, r.Value, r.Unit);
                continue;
            }

            db.Readings.Add(reading!);
            added++;
        }

        if (added == 0)
        {
            return 0;
        }

        try
        {
            await db.SaveChangesAsync(ct);
            return added;
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            // Another pod stored some of these between our lookup and our insert; the rest come next run.
            logger.LogInformation("Readings of meter {MeterId} were stored by another instance", meter.Id);
            return 0;
        }
    }
}
