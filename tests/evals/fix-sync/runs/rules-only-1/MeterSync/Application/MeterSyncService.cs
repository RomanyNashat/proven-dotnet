using MeterSync.Domain;
using MeterSync.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MeterSync.Application;

/// <summary>Pulls new readings from the vendor for every linked meter.</summary>
public sealed class MeterSyncService(SyncDbContext db, MeterVendorClient vendor, ILogger<MeterSyncService> logger)
{
    private const int VendorIdMaxLength = 64;

    /// <summary>Imports the readings the service doesn't have yet. Safe to run on several pods at once.</summary>
    /// <param name="ct">Cancels the run.</param>
    /// <returns>How many readings this call stored.</returns>
    public async Task<int> SyncOnceAsync(CancellationToken ct = default)
    {
        var meters = await db.Meters.AsNoTracking().OrderBy(m => m.Id).ToListAsync(ct);
        var added = 0;
        foreach (var meter in meters)
        {
            // One meter the vendor can't answer for must not stop the others.
            try
            {
                added += await SyncMeterAsync(meter, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                db.ChangeTracker.Clear();
                logger.LogError(ex, "Sync failed for meter {MeterId}", meter.Id);
            }
        }

        return added;
    }

    private async Task<int> SyncMeterAsync(Meter meter, CancellationToken ct)
    {
        var fromVendor = (await vendor.ReadingsAsync(meter.SerialNumber, ct))
            .Where(r => !string.IsNullOrWhiteSpace(r.Id) && r.Id.Length <= VendorIdMaxLength && r.TakenAt is not null)
            .DistinctBy(r => r.Id, StringComparer.Ordinal)
            .ToList();
        if (fromVendor.Count == 0)
        {
            return 0;
        }

        // The vendor id is unique across the vendor, and the index on it is too: check all readings, not this meter's.
        var ids = fromVendor.Select(r => r.Id!).ToList();
        var known = (await db.Readings.AsNoTracking()
                .Where(r => ids.Contains(r.VendorId))
                .Select(r => r.VendorId)
                .ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);

        var added = 0;
        foreach (var r in fromVendor.Where(r => !known.Contains(r.Id!)))
        {
            if (!GlucoseValue.TryParse(r.Value, r.Unit, out var parsed))
            {
                // The value is health data, so it stays out of the log.
                logger.LogWarning("Skipping reading {VendorId} of meter {MeterId}: the value or unit isn't one the vendor documents", r.Id, meter.Id);
                continue;
            }

            db.Readings.Add(Reading.FromMeter(
                meter.PatientId, meter.Id, r.Id!, parsed.Raw, parsed.Unit, parsed.MgDl, r.TakenAt!.Value.ToUniversalTime()));
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
            // Another pod stored them first. Anything it didn't store is picked up on the next run.
            db.ChangeTracker.Clear();
            logger.LogInformation("Readings of meter {MeterId} were stored by another instance", meter.Id);
            return 0;
        }
    }
}
