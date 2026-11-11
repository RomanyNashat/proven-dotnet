using MeterSync.Domain;
using MeterSync.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MeterSync.Application;

/// <summary>Pulls new readings from the vendor for every linked meter.</summary>
public sealed class MeterSyncService(SyncDbContext db, MeterVendorClient vendor, ILogger<MeterSyncService> logger)
{
    /// <summary>Stores the readings the vendor has that this service doesn't yet.</summary>
    /// <param name="ct">Stops the run.</param>
    /// <returns>The number of readings this call inserted.</returns>
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
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // Another pod stored the same readings first; nothing is lost, they are just not ours to count.
                logger.LogInformation("Readings of meter {MeterId} were stored by another instance", meter.Id);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // The serial is not logged: it identifies a patient's device.
                logger.LogError(ex, "Sync of meter {MeterId} failed", meter.Id);
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
        var fromVendor = await vendor.ReadingsAsync(meter.SerialNumber, ct);
        var parsed = new List<ParsedVendorReading>();
        foreach (var reading in fromVendor.DistinctBy(r => r.Id, StringComparer.Ordinal))
        {
            if (VendorReadingParser.TryParse(reading, out var ok, out var reason))
            {
                parsed.Add(ok);
            }
            else
            {
                logger.LogWarning("Skipping vendor reading {VendorId} of meter {MeterId}: {Reason}", reading.Id, meter.Id, reason);
            }
        }

        if (parsed.Count == 0)
        {
            return 0;
        }

        var ids = parsed.Select(p => p.VendorId).ToList();
        var known = await db.Readings
            .Where(r => ids.Contains(r.VendorId))
            .Select(r => r.VendorId)
            .ToHashSetAsync(StringComparer.Ordinal, ct);
        var fresh = parsed.Where(p => !known.Contains(p.VendorId)).Select(p => p.ToReading(meter.PatientId, meter.Id)).ToList();
        if (fresh.Count == 0)
        {
            return 0;
        }

        db.Readings.AddRange(fresh);
        await db.SaveChangesAsync(ct);
        return fresh.Count;
    }
}
