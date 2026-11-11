using System.Text.Json;
using MeterSync.Domain;
using MeterSync.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MeterSync.Application;

/// <summary>Pulls new readings from the vendor for every linked meter.</summary>
public sealed class MeterSyncService(SyncDbContext db, MeterVendorClient vendor, ILogger<MeterSyncService> logger)
{
    // First key of pg_advisory_xact_lock(int, int); the second is the meter id. Unique to this job.
    private const int MeterLockNamespace = 0x4D53_5931;

    /// <summary>Stores the readings the vendor has that this service hasn't seen yet.</summary>
    /// <param name="ct">Stops the run.</param>
    /// <returns>The number of readings added.</returns>
    public async Task<int> SyncOnceAsync(CancellationToken ct = default)
    {
        var meters = await db.Meters.AsNoTracking().OrderBy(m => m.Id).ToListAsync(ct);
        var added = 0;
        foreach (var meter in meters)
        {
            var fetched = await FetchAsync(meter, ct);
            if (fetched.Count > 0)
            {
                added += await StoreAsync(meter, fetched, ct);
            }
        }

        return added;
    }

    private async Task<IReadOnlyList<VendorReading>> FetchAsync(Meter meter, CancellationToken ct)
    {
        try
        {
            return await vendor.ReadingsAsync(meter.SerialNumber, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException
                                   || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            // One meter's failure must not stop the others.
            logger.LogWarning("Fetching readings for meter {MeterId} failed ({ErrorType}); skipping it this run", meter.Id, ex.GetType().Name);
            return [];
        }
    }

    private async Task<int> StoreAsync(Meter meter, IReadOnlyList<VendorReading> fetched, CancellationToken ct)
    {
        var candidates = new Dictionary<string, Reading>(StringComparer.Ordinal);
        foreach (var r in fetched)
        {
            var reading = Reading.FromVendor(meter.PatientId, meter.Id, r.Id, r.Value, r.Unit, r.TakenAt);
            if (reading is null)
            {
                // Only the vendor id: the value is health data.
                logger.LogWarning("Skipping vendor reading {VendorId} from meter {MeterId}: unusable value, unit or id", r.Id, meter.Id);
                continue;
            }

            candidates.TryAdd(reading.VendorId, reading);
        }

        if (candidates.Count == 0)
        {
            return 0;
        }

        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // Two pods syncing the same meter queue here; the second then sees the first's committed rows.
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({MeterLockNamespace}, {meter.Id})", ct);

            var ids = candidates.Keys.ToList();
            var known = (await db.Readings.AsNoTracking()
                    .Where(r => ids.Contains(r.VendorId))
                    .Select(r => r.VendorId)
                    .ToListAsync(ct))
                .ToHashSet(StringComparer.Ordinal);

            var fresh = candidates.Values.Where(r => !known.Contains(r.VendorId)).ToList();
            db.Readings.AddRange(fresh);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return fresh.Count;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Safety net: another writer stored one of these readings. The next run picks up the rest.
            logger.LogWarning("Readings for meter {MeterId} were stored concurrently; skipping this batch", meter.Id);
            return 0;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }
}
