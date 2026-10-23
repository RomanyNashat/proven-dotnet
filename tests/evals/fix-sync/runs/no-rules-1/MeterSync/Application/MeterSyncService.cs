using System.Globalization;
using System.Text.Json;
using MeterSync.Domain;
using MeterSync.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MeterSync.Application;

/// <summary>Pulls new readings from the vendor for every linked meter.</summary>
public sealed class MeterSyncService(SyncDbContext db, MeterVendorClient vendor, ILogger<MeterSyncService> logger)
{
    private const decimal MgDlPerMmolL = 18m;

    // Just outside the meter's range (VENDOR.md), so HI always alerts high and LO always alerts low.
    private const decimal HiMgDl = 601m;
    private const decimal LoMgDl = 19m;
    private const decimal MaxMgDl = 9999m; // numeric(6,1)

    public async Task<int> SyncOnceAsync(CancellationToken ct = default)
    {
        var meters = await db.Meters.AsNoTracking().ToListAsync(ct);
        var added = 0;
        foreach (var meter in meters)
        {
            IReadOnlyList<VendorReading> readings;
            try
            {
                readings = await vendor.ReadingsAsync(meter.SerialNumber, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                // One meter the vendor can't serve must not stop the others.
                logger.LogError(ex, "Vendor readings for meter {MeterId} failed", meter.Id);
                continue;
            }

            added += await StoreAsync(meter, readings, ct);
        }

        return added;
    }

    /// <summary>Converts a vendor value to mg/dL, or returns false when it can't be read.</summary>
    public static bool TryToMgDl(string raw, string unit, out decimal mgDl)
    {
        mgDl = 0;
        if (string.Equals(raw, "HI", StringComparison.OrdinalIgnoreCase))
        {
            mgDl = HiMgDl;
            return true;
        }

        if (string.Equals(raw, "LO", StringComparison.OrdinalIgnoreCase))
        {
            mgDl = LoMgDl;
            return true;
        }

        if (!decimal.TryParse(raw, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        if (string.Equals(unit, "mmol/L", StringComparison.OrdinalIgnoreCase))
        {
            value *= MgDlPerMmolL;
        }
        else if (!string.Equals(unit, "mg/dL", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        mgDl = Math.Round(value, 1, MidpointRounding.AwayFromZero);
        return mgDl <= MaxMgDl;
    }

    private async Task<int> StoreAsync(Meter meter, IReadOnlyList<VendorReading> readings, CancellationToken ct)
    {
        var ids = readings.Select(r => r.Id).Where(id => id is not null).Distinct().ToList();
        // Two pods may sync the same meter at once; the unique VendorId index decides, and the loser re-reads.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var known = (await db.Readings.AsNoTracking().Where(r => ids.Contains(r.VendorId)).Select(r => r.VendorId).ToListAsync(ct))
                .ToHashSet();
            var fresh = new List<Reading>();
            foreach (var r in readings)
            {
                if (string.IsNullOrEmpty(r.Id) || r.Id.Length > 64 || !known.Add(r.Id))
                {
                    continue;
                }

                if (r.Value is null || r.Unit is null || r.Value.Length > 16 || r.Unit.Length > 8
                    || !TryToMgDl(r.Value, r.Unit, out var mgDl))
                {
                    logger.LogWarning("Skipping reading {VendorId}: {Value} {Unit} can't be read", r.Id, r.Value, r.Unit);
                    continue;
                }

                fresh.Add(Reading.FromMeter(meter.PatientId, meter.Id, r.Id, r.Value, r.Unit, mgDl, r.TakenAt.ToUniversalTime()));
            }

            if (fresh.Count == 0)
            {
                return 0;
            }

            db.Readings.AddRange(fresh);
            try
            {
                await db.SaveChangesAsync(ct);
                return fresh.Count;
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                db.ChangeTracker.Clear();
            }
        }

        logger.LogWarning("Readings of meter {MeterId} kept conflicting; they will be retried on the next run", meter.Id);
        return 0;
    }
}
