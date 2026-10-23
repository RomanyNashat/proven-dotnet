using MeterSync.Domain;
using MeterSync.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MeterSync.Application;

/// <summary>Pulls new readings from the vendor for every linked meter.</summary>
public sealed class MeterSyncService(SyncDbContext db, MeterVendorClient vendor, ILogger<MeterSyncService> logger)
{
    // Arbitrary key shared by every pod, so only one of them syncs at a time.
    private const long SyncLockKey = 7_302_001;

    private const int MaxVendorIdLength = 64;
    private const int MaxRawLength = 16;
    private const int MaxUnitLength = 8;

    public async Task<int> SyncOnceAsync(CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var locked = await db.Database.SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock({SyncLockKey}) AS \"Value\"").SingleAsync(ct);
        if (!locked)
        {
            return 0;
        }

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
                // One bad meter or vendor hiccup must not stop the others.
                db.ChangeTracker.Clear();
                logger.LogError(ex, "Syncing meter {MeterId} failed", meter.Id);
            }
        }

        await tx.CommitAsync(ct);
        return added;
    }

    private async Task<int> SyncMeterAsync(Meter meter, CancellationToken ct)
    {
        var readings = await vendor.ReadingsAsync(meter.SerialNumber, ct);
        var ids = readings.Select(r => r.Id).Where(id => !string.IsNullOrEmpty(id) && id.Length <= MaxVendorIdLength).Distinct().ToList();
        var known = (await db.Readings.Where(r => ids.Contains(r.VendorId)).Select(r => r.VendorId).ToListAsync(ct)).ToHashSet();

        var added = 0;
        foreach (var r in readings)
        {
            if (string.IsNullOrEmpty(r.Id) || r.Id.Length > MaxVendorIdLength || known.Contains(r.Id))
            {
                continue;
            }

            if ((r.Value?.Trim().Length ?? 0) > MaxRawLength || (r.Unit?.Trim().Length ?? 0) > MaxUnitLength
                || !VendorReadingParser.TryParse(r, out var mgDl))
            {
                logger.LogWarning("Skipping reading {VendorId}: unusable value {Value} {Unit}", r.Id, r.Value, r.Unit);
                continue;
            }

            known.Add(r.Id);
            db.Readings.Add(Reading.FromMeter(meter.PatientId, meter.Id, r.Id, r.Value!.Trim(), r.Unit!.Trim(), mgDl, r.TakenAt.ToUniversalTime()));
            added++;
        }

        await db.SaveChangesAsync(ct);
        return added;
    }
}
