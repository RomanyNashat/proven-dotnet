using MeterSync.Domain;
using MeterSync.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MeterSync.Application;

/// <summary>Pulls new readings from the vendor for every linked meter.</summary>
public sealed class MeterSyncService(SyncDbContext db, MeterVendorClient vendor, TimeProvider time, ILogger<MeterSyncService> logger)
{
    /// <summary>Stores the readings the vendor has that we don't, meter by meter.</summary>
    /// <returns>The number of readings added.</returns>
    public async Task<int> SyncOnceAsync(CancellationToken ct = default)
    {
        var meters = await db.Meters.AsNoTracking().OrderBy(m => m.Id).ToListAsync(ct);
        var added = 0;
        foreach (var meter in meters)
        {
            // One meter failing (vendor 404, timeout, bad data) must not stop the others.
            try
            {
                added += await SyncMeterAsync(meter, ct);
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning(ex, "Could not fetch readings for meter {MeterId}", meter.Id);
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Timed out fetching readings for meter {MeterId}", meter.Id);
            }
            catch (System.Text.Json.JsonException ex)
            {
                logger.LogWarning(ex, "The vendor sent unreadable readings for meter {MeterId}", meter.Id);
            }
        }

        return added;
    }

    private async Task<int> SyncMeterAsync(Meter meter, CancellationToken ct)
    {
        var readings = (await vendor.ReadingsAsync(meter.SerialNumber, ct))
            .Where(r => !string.IsNullOrWhiteSpace(r.Id) && r.Id.Length <= SyncDbContext.VendorIdMaxLength)
            .DistinctBy(r => r.Id, StringComparer.Ordinal)
            .ToList();
        if (readings.Count == 0)
        {
            return 0;
        }

        // Vendor ids are unique across the vendor, so check against every stored reading, not just this meter's.
        var ids = readings.Select(r => r.Id).ToList();
        var known = (await db.Readings.Where(r => ids.Contains(r.VendorId)).Select(r => r.VendorId).ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);

        var now = time.GetUtcNow();
        var added = 0;
        foreach (var r in readings.Where(r => !known.Contains(r.Id)))
        {
            if (r.Value is null || r.Value.Trim().Length > SyncDbContext.RawMaxLength
                || !Glucose.TryParse(r.Value, r.Unit, out var glucose))
            {
                // The value itself is a health record: log the id, not the value.
                logger.LogWarning("Skipping reading {VendorId} of meter {MeterId}: unrecognised value or unit", r.Id, meter.Id);
                continue;
            }

            db.Readings.Add(Reading.FromMeter(meter.PatientId, meter.Id, r.Id, r.Value, glucose, r.TakenAt, now));
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
            // Another pod stored this meter's new readings between our check and our insert. They're in; move on.
            logger.LogInformation("Readings for meter {MeterId} were stored by another instance", meter.Id);
            return 0;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }
}
