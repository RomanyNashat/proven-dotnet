using MeterSync.Domain;
using MeterSync.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MeterSync.Application;

/// <summary>Pulls new readings from the vendor for every linked meter.</summary>
/// <remarks>
/// Every pod runs this. The unique index on the vendor's reading id is what keeps a reading from being
/// stored twice when two pods sync the same meter at once; the pod that loses the race skips that meter
/// for this run.
/// </remarks>
public sealed class MeterSyncService(SyncDbContext db, MeterVendorClient vendor, ILogger<MeterSyncService> logger)
{
    /// <summary>Syncs every linked meter once.</summary>
    /// <param name="ct">Cancels the run.</param>
    /// <returns>How many new readings were stored.</returns>
    public async Task<int> SyncOnceAsync(CancellationToken ct = default)
    {
        var meters = await db.Meters.AsNoTracking().OrderBy(m => m.Id).ToListAsync(ct);
        var added = 0;
        foreach (var meter in meters)
        {
            // One meter failing (vendor error, a bad payload) must not stop the others.
            try
            {
                added += await SyncMeterAsync(meter, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                db.ChangeTracker.Clear();
                logger.LogError(ex, "Sync of meter {MeterId} failed", meter.Id);
            }
        }

        return added;
    }

    private async Task<int> SyncMeterAsync(Meter meter, CancellationToken ct)
    {
        var readings = await vendor.ReadingsAsync(meter.SerialNumber, ct);
        if (readings.Count == 0)
        {
            return 0;
        }

        var vendorIds = readings.Select(r => r.Id).Distinct().ToList();
        var known = (await db.Readings
                .Where(r => vendorIds.Contains(r.VendorId))
                .Select(r => r.VendorId)
                .ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);

        var added = 0;
        foreach (var r in readings)
        {
            if (string.IsNullOrWhiteSpace(r.Id) || r.Id.Length > 64 || !known.Add(r.Id))
            {
                continue;
            }

            var value = GlucoseValue.Parse(r.Value, r.Unit);
            if (value is null || r.Value.Length > 16)
            {
                // The value itself is health data: log the ids only.
                logger.LogWarning("Skipping reading {VendorId} of meter {MeterId}: not a value and unit we can read", r.Id, meter.Id);
                continue;
            }

            db.Readings.Add(Reading.FromMeter(meter.PatientId, meter.Id, r.Id, r.Value.Trim(), r.Unit, value, r.TakenAt, meter.LinkedAt));
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
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Another pod stored these readings first. What it missed is picked up on the next run.
            db.ChangeTracker.Clear();
            logger.LogInformation("Meter {MeterId}: readings already stored by another instance", meter.Id);
            return 0;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }
}
