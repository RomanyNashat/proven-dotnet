using MeterSync.Domain;
using MeterSync.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MeterSync.Application;

/// <summary>Pulls new readings from the vendor for every linked meter.</summary>
public sealed class MeterSyncService(
    SyncDbContext db, MeterVendorClient vendor, TimeProvider time, ILogger<MeterSyncService> logger)
{
    /// <summary>
    /// An out-of-range reading older than this when we first see it is stored but not texted: the vendor
    /// returns a meter's whole history, so a freshly linked meter (or a long outage) would otherwise text
    /// the patient about every high or low of the past months.
    /// </summary>
    public static readonly TimeSpan AlertWindow = TimeSpan.FromHours(6);

    /// <summary>Stores the readings the vendor has that we don't, meter by meter.</summary>
    /// <param name="ct">Cancels the run.</param>
    /// <returns>How many readings were added.</returns>
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
            catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or DbUpdateException
                                       || (ex is OperationCanceledException && !ct.IsCancellationRequested))
            {
                // One meter failing (vendor error, timeout, bad payload) must not stop the others.
                db.ChangeTracker.Clear();
                logger.LogWarning(ex, "Sync of meter {MeterId} failed", meter.Id);
            }
        }

        return added;
    }

    private async Task<int> SyncMeterAsync(Meter meter, CancellationToken ct)
    {
        var readings = await vendor.ReadingsAsync(meter.SerialNumber, ct);
        try
        {
            return await AddNewAsync(meter, readings, ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Another pod stored some of these readings between our check and our insert. Store what's left.
            db.ChangeTracker.Clear();
            return await AddNewAsync(meter, readings, ct);
        }
    }

    private async Task<int> AddNewAsync(Meter meter, IReadOnlyList<VendorReading> readings, CancellationToken ct)
    {
        var ids = readings.Select(r => r.Id).Distinct().ToList();
        var known = (await db.Readings
                .Where(r => ids.Contains(r.VendorId))
                .Select(r => r.VendorId)
                .ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);

        var now = time.GetUtcNow();
        var added = 0;
        foreach (var r in readings)
        {
            if (!known.Add(r.Id))
            {
                continue;
            }

            if (r.Id.Length is 0 or > 64 || r.Value.Length > 16)
            {
                logger.LogWarning("Skipping reading {VendorId} of meter {MeterId}: id or value out of bounds", r.Id, meter.Id);
                continue;
            }

            var takenAt = r.TakenAt.ToUniversalTime();
            var worthTexting = takenAt >= meter.LinkedAt && now - takenAt <= AlertWindow;
            var reading = Reading.TryFromMeter(meter.PatientId, meter.Id, r.Id, r.Value, r.Unit, takenAt, worthTexting);
            if (reading is null)
            {
                logger.LogWarning("Skipping reading {VendorId} of meter {MeterId}: can't read {Value} {Unit}", r.Id, meter.Id, r.Value, r.Unit);
                continue;
            }

            db.Readings.Add(reading);
            added++;
        }

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return added;
    }
}
