using MeterSync.Domain;
using MeterSync.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MeterSync.Application;

/// <summary>Pulls new readings from the vendor for every linked meter.</summary>
public sealed class MeterSyncService(SyncDbContext db, MeterVendorClient vendor, ILogger<MeterSyncService> logger)
{
    /// <summary>Stores readings the service hasn't seen. Safe to run on several pods at once.</summary>
    /// <param name="ct">Stops the run.</param>
    /// <returns>How many readings this call stored.</returns>
    public async Task<int> SyncOnceAsync(CancellationToken ct = default)
    {
        var meters = await db.Meters.AsNoTracking().OrderBy(m => m.Id).ToListAsync(ct);
        var added = 0;
        foreach (var meter in meters)
        {
            // One meter whose vendor call fails must not stop the others syncing.
            try
            {
                added += await SyncMeterAsync(meter, ct);
            }
            catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
                                       && !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Sync of meter {MeterId} failed; it is retried on the next run", meter.Id);
            }
        }

        return added;
    }

    private async Task<int> SyncMeterAsync(Meter meter, CancellationToken ct)
    {
        var readings = await vendor.ReadingsAsync(meter.SerialNumber, ct);
        var known = (await db.Readings.AsNoTracking()
                .Where(r => r.MeterId == meter.Id)
                .Select(r => r.VendorId)
                .ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);

        var added = 0;
        foreach (var r in readings.Where(r => !string.IsNullOrWhiteSpace(r.Id) && !known.Contains(r.Id)).DistinctBy(r => r.Id))
        {
            // The columns are bounded; an oversized value would fail the insert for the whole meter.
            if (r.Id.Length > 64 || r.Value?.Length > 16 || r.Unit?.Length > 8 || !Glucose.TryToMgDl(r.Value, r.Unit, out var mgDl))
            {
                logger.LogWarning("Skipping reading {VendorId}: value {Value} {Unit} can't be read", r.Id, r.Value, r.Unit);
                continue;
            }

            var reading = Reading.FromMeter(meter.PatientId, meter.Id, r.Id, r.Value, r.Unit, mgDl, r.TakenAt.ToUniversalTime());
            added += await InsertIfNewAsync(reading, ct);
        }

        return added;
    }

    // Another pod may store the same reading between our read of `known` and this insert; ON CONFLICT makes
    // that a no-op instead of a unique-index failure. Column names are EF's defaults for the model
    // (checked by SyncDbContextModelTests).
    private Task<int> InsertIfNewAsync(Reading r, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync($"""
            INSERT INTO readings ("PatientId", "MeterId", "VendorId", "Raw", "Unit", "ValueMgDl", "TakenAt", "Alert")
            VALUES ({r.PatientId}, {r.MeterId}, {r.VendorId}, {r.Raw}, {r.Unit}, {r.ValueMgDl}, {r.TakenAt}, {r.Alert.ToString()})
            ON CONFLICT ("VendorId") DO NOTHING
            """, ct);
}
