using MeterSync.Domain;
using MeterSync.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace MeterSync.Application;

/// <summary>Pulls new readings from the vendor for every linked meter.</summary>
public sealed class MeterSyncService(SyncDbContext db, MeterVendorClient vendor, ILogger<MeterSyncService> logger)
{
    /// <summary>Syncs every linked meter once. A failing meter is logged and skipped.</summary>
    /// <param name="ct">Cancels the run.</param>
    /// <returns>The number of readings inserted by this call.</returns>
    public async Task<int> SyncOnceAsync(CancellationToken ct = default)
    {
        var meters = await db.Meters.AsNoTracking().OrderBy(m => m.Id).ToListAsync(ct);
        var inserted = 0;
        foreach (var meter in meters)
        {
            try
            {
                inserted += await SyncMeterAsync(meter, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // One meter the vendor doesn't know (404) or times out on must not stop the others.
                logger.LogError(ex, "Sync of meter {MeterId} failed", meter.Id);
            }
        }

        return inserted;
    }

    private async Task<int> SyncMeterAsync(Meter meter, CancellationToken ct)
    {
        var vendorReadings = await vendor.ReadingsAsync(meter.SerialNumber, ct);
        var incoming = vendorReadings.Select(r => r.Id).Where(id => id is not null).Distinct().ToList();
        var known = (await db.Readings
                .Where(r => incoming.Contains(r.VendorId))
                .Select(r => r.VendorId)
                .ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);

        var fresh = new List<Reading>();
        foreach (var r in vendorReadings.Where(r => r.Id is not null && known.Add(r.Id)))
        {
            if (Reading.TryFromMeter(meter, r.Id, r.Value, r.Unit, r.TakenAt, out var reading, out var rejection))
            {
                fresh.Add(reading);
            }
            else
            {
                logger.LogWarning("Skipping reading {VendorId}: {Reason}", r.Id.Length <= Reading.VendorIdMaxLength ? r.Id : "(too long)", rejection);
            }
        }

        if (fresh.Count == 0)
        {
            return 0;
        }

        // Same order on every pod, so two pods inserting the same meter's batch can't deadlock.
        fresh.Sort((a, b) => string.CompareOrdinal(a.VendorId, b.VendorId));
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var inserted = 0;
        foreach (var reading in fresh)
        {
            inserted += await InsertIfNewAsync(reading, ct);
        }

        await transaction.CommitAsync(ct);
        return inserted;
    }

    // ON CONFLICT DO NOTHING: another pod may insert the same vendor reading between our check and
    // our insert; that must skip the row, not roll back this meter's whole batch.
    private Task<int> InsertIfNewAsync(Reading r, CancellationToken ct)
    {
        var value = new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Numeric, Value = (object?)r.ValueMgDl ?? DBNull.Value };
        return db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO readings ("PatientId", "MeterId", "VendorId", "Raw", "Unit", "ValueMgDl", "TakenAt", "Alert", "AlertAttempts")
            VALUES ({r.PatientId}, {r.MeterId}, {r.VendorId}, {r.Raw}, {r.Unit}, {value}, {r.TakenAt}, {r.Alert.ToString()}, {r.AlertAttempts})
            ON CONFLICT ("VendorId") DO NOTHING
            """,
            ct);
    }
}
