using MeterSync.Domain;
using MeterSync.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MeterSync.Application;

/// <summary>Pulls new readings from the vendor for every linked meter.</summary>
public sealed class MeterSyncService(SyncDbContext db, MeterVendorClient vendor, ILogger<MeterSyncService> logger)
{
    /// <summary>Imports the readings the service doesn't have yet. Safe to run on several pods at once.</summary>
    /// <returns>How many readings this call inserted.</returns>
    public async Task<int> SyncOnceAsync(CancellationToken ct = default)
    {
        var meters = await db.Meters.AsNoTracking().OrderBy(m => m.Id).ToListAsync(ct);
        var added = 0;
        foreach (var meter in meters)
        {
            // One meter failing (vendor error, timeout, bad data) must not stop the others.
            try
            {
                added += await SyncMeterAsync(meter, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError(ex, "Sync failed for meter {MeterId}", meter.Id);
            }
        }

        return added;
    }

    private async Task<int> SyncMeterAsync(Meter meter, CancellationToken ct)
    {
        var incoming = await vendor.ReadingsAsync(meter.SerialNumber, ct);
        var incomingIds = incoming.Select(r => r.Id).Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
        var known = (await db.Readings
                .Where(r => incomingIds.Contains(r.VendorId))
                .Select(r => r.VendorId)
                .ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);

        var added = 0;
        foreach (var r in incoming.Where(r => !known.Contains(r.Id)))
        {
            if (Reading.TryFromMeter(meter.PatientId, meter.Id, r.Id, r.Value, r.Unit, r.TakenAt) is not { } reading)
            {
                // A health value is personal data: log which reading, never what it said.
                logger.LogWarning("Skipping reading {VendorId} of meter {MeterId}: unreadable value or unit", r.Id, meter.Id);
                continue;
            }

            added += await InsertAsync(reading, ct);
        }

        return added;
    }

    // Another pod may import the same reading at the same moment. ON CONFLICT makes the insert a
    // no-op for the loser instead of a unique violation, and each row stands alone, so one conflict
    // doesn't throw away the rest of the run. Interpolated values become parameters.
    private Task<int> InsertAsync(Reading reading, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO readings ("PatientId", "MeterId", "VendorId", "Raw", "Unit", "ValueMgDl", "TakenAt", "Alert")
            VALUES ({reading.PatientId}, {reading.MeterId}, {reading.VendorId}, {reading.Raw}, {reading.Unit},
                    {reading.ValueMgDl}, {reading.TakenAt}, {reading.Alert.ToString()})
            ON CONFLICT ("VendorId") DO NOTHING
            """,
            ct);
}
