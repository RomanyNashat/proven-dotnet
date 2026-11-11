using MeterSync.Domain;
using MeterSync.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MeterSync.Application;

/// <summary>Pulls new readings from the vendor for every linked meter.</summary>
public sealed class MeterSyncService(SyncDbContext db, MeterVendorClient vendor, ILogger<MeterSyncService> logger)
{
    /// <summary>Fetches every meter's readings and stores the ones not seen before.</summary>
    /// <param name="ct">Cancels the run.</param>
    /// <returns>How many readings this run stored.</returns>
    public async Task<int> SyncOnceAsync(CancellationToken ct = default)
    {
        var meters = await db.Meters.AsNoTracking().OrderBy(m => m.Id).ToListAsync(ct);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var added = 0;
        foreach (var meter in meters)
        {
            IReadOnlyList<VendorReading> readings;
            try
            {
                readings = await vendor.ReadingsAsync(meter.SerialNumber, ct);
            }
            catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
                                       && !ct.IsCancellationRequested)
            {
                // One meter the vendor can't serve must not stop every other patient's readings.
                logger.LogWarning(ex, "Couldn't fetch readings for meter {MeterId}", meter.Id);
                continue;
            }

            // The vendor ids are unique across the vendor; a duplicate in one response would fail the whole insert.
            var fresh = readings
                .Where(r => !string.IsNullOrWhiteSpace(r.Id) && r.Id.Length <= 64 && seen.Add(r.Id))
                .ToList();
            var ids = fresh.Select(r => r.Id).ToList();
            var known = await db.Readings.Where(r => ids.Contains(r.VendorId)).Select(r => r.VendorId).ToListAsync(ct);
            var knownSet = known.ToHashSet(StringComparer.Ordinal);

            foreach (var r in fresh.Where(r => !knownSet.Contains(r.Id)))
            {
                // The vendor returns everything the meter ever uploaded; what came before the link isn't this
                // patient's (or is old history) and must not trigger an SMS.
                if (r.TakenAt < meter.LinkedAt)
                {
                    continue;
                }

                var raw = (r.Value ?? "").Trim();
                var unit = (r.Unit ?? "").Trim();
                var level = GlucoseLevel.Parse(raw, unit);
                if (level is null || raw.Length > 16 || unit.Length > 8)
                {
                    logger.LogWarning("Skipping reading {VendorId}: can't read {Value} {Unit}", r.Id, raw, unit);
                    continue;
                }

                db.Readings.Add(Reading.FromMeter(meter.PatientId, meter.Id, r.Id, raw, unit, level, r.TakenAt));
                added++;
            }
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Another pod stored the same readings first. Nothing is lost: what it didn't store comes next run.
            db.ChangeTracker.Clear();
            logger.LogInformation("Another instance stored these readings first; skipping this run");
            return 0;
        }

        return added;
    }
}
