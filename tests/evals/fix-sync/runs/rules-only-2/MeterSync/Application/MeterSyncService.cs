using MeterSync.Domain;
using MeterSync.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MeterSync.Application;

/// <summary>Pulls new readings from the vendor for every linked meter.</summary>
public sealed class MeterSyncService(SyncDbContext db, MeterVendorClient vendor, ILogger<MeterSyncService> logger)
{
    // Two pods sync the same meters at the same time; the loser of a race retries against what the winner saved.
    private const int Attempts = 3;

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
                // One meter the vendor can't serve must not stop every other patient's readings.
                db.ChangeTracker.Clear();
                logger.LogWarning(ex, "Syncing meter {MeterId} failed; the next run tries again", meter.Id);
            }
        }

        return added;
    }

    private async Task<int> SyncMeterAsync(Meter meter, CancellationToken ct)
    {
        var incoming = (await vendor.ReadingsAsync(meter.SerialNumber, ct))
            .Where(r => !string.IsNullOrWhiteSpace(r.Id))
            .DistinctBy(r => r.Id)
            .ToList();
        if (incoming.Count == 0)
        {
            return 0;
        }

        for (var attempt = 1; ; attempt++)
        {
            var ids = incoming.Select(r => r.Id).ToList();
            var known = (await db.Readings.Where(r => ids.Contains(r.VendorId)).Select(r => r.VendorId).ToListAsync(ct))
                .ToHashSet(StringComparer.Ordinal);

            var fresh = new List<Reading>();
            foreach (var r in incoming.Where(r => !known.Contains(r.Id)))
            {
                if (ToReading(meter, r) is { } reading)
                {
                    fresh.Add(reading);
                }
            }

            if (fresh.Count == 0)
            {
                return 0;
            }

            db.Readings.AddRange(fresh);
            try
            {
                await db.SaveChangesAsync(ct);
                db.ChangeTracker.Clear();
                return fresh.Count;
            }
            catch (DbUpdateException ex) when (attempt < Attempts && IsUniqueViolation(ex))
            {
                db.ChangeTracker.Clear();
                logger.LogInformation("Another instance saved readings for meter {MeterId} first; retrying", meter.Id);
            }
        }
    }

    private Reading? ToReading(Meter meter, VendorReading r)
    {
        if (r.Id.Length > 64 || r.Value is null || r.Value.Length > 16 || r.Unit is null || r.Unit.Length > 8)
        {
            logger.LogWarning("Skipping reading {VendorId} of meter {MeterId}: a field is longer than the vendor documents", r.Id, meter.Id);
            return null;
        }

        if (!GlucoseValue.TryToMgDl(r.Value, r.Unit, out var mgDl))
        {
            // The value itself is health data: it stays out of the log.
            logger.LogWarning("Skipping reading {VendorId} of meter {MeterId}: the value or unit isn't one the vendor documents", r.Id, meter.Id);
            return null;
        }

        return Reading.FromMeter(meter.PatientId, meter.Id, meter.LinkedAt, r.Id, r.Value.Trim(), r.Unit, mgDl, r.TakenAt);
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
