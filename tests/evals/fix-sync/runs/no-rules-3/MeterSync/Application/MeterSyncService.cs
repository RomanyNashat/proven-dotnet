using System.Globalization;
using MeterSync.Domain;
using MeterSync.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MeterSync.Application;

/// <summary>Pulls new readings from the vendor for every linked meter.</summary>
public sealed class MeterSyncService(SyncDbContext db, MeterVendorClient vendor, ILogger<MeterSyncService> logger)
{
    private const decimal MgDlPerMmolL = 18m;

    // The meter shows HI above 600 mg/dL and LO below 20 mg/dL; the true value is unknown, so store the bound.
    private const decimal HiMgDl = 600m;
    private const decimal LoMgDl = 20m;

    // Reading.ValueMgDl is numeric(6,1).
    private const decimal MaxMgDl = 99999m;

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
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One meter's vendor failure must not stop the others from syncing.
                logger.LogError(ex, "Sync failed for meter {MeterId}", meter.Id);
                db.ChangeTracker.Clear();
            }
        }

        return added;
    }

    private async Task<int> SyncMeterAsync(Meter meter, CancellationToken ct)
    {
        var readings = await vendor.ReadingsAsync(meter.SerialNumber, ct);
        var ids = readings.Select(r => r.Id).Distinct().ToArray();
        // Vendor ids are unique across the vendor, so check them globally (the unique index does).
        var known = (await db.Readings.Where(r => ids.Contains(r.VendorId)).Select(r => r.VendorId).ToListAsync(ct)).ToHashSet();

        var added = 0;
        foreach (var r in readings)
        {
            if (string.IsNullOrEmpty(r.Id) || r.Id.Length > 64 || !known.Add(r.Id))
            {
                continue;
            }

            if (!TryToMgDl(r, out var value))
            {
                logger.LogWarning("Skipping reading {VendorId}: {Value} {Unit} can't be interpreted", r.Id, r.Value, r.Unit);
                continue;
            }

            db.Readings.Add(Reading.FromMeter(meter.PatientId, meter.Id, r.Id, r.Value.Trim(), r.Unit, value, r.TakenAt.ToUniversalTime()));
            try
            {
                await db.SaveChangesAsync(ct);
                added++;
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // Another pod stored this reading between our check and our insert.
                db.ChangeTracker.Clear();
            }
        }

        return added;
    }

    /// <summary>The reading in mg/dL (the unit the care team's thresholds are in).</summary>
    private static bool TryToMgDl(VendorReading r, out decimal mgDl)
    {
        mgDl = 0;
        if (r.Value is null || r.Value.Length > 16 || r.Unit is null || r.Unit.Length > 8)
        {
            return false;
        }

        decimal factor;
        if (r.Unit.Equals("mg/dL", StringComparison.OrdinalIgnoreCase))
        {
            factor = 1m;
        }
        else if (r.Unit.Equals("mmol/L", StringComparison.OrdinalIgnoreCase))
        {
            factor = MgDlPerMmolL;
        }
        else
        {
            return false;
        }

        var raw = r.Value.Trim();
        if (raw.Equals("HI", StringComparison.OrdinalIgnoreCase))
        {
            mgDl = HiMgDl;
            return true;
        }

        if (raw.Equals("LO", StringComparison.OrdinalIgnoreCase))
        {
            mgDl = LoMgDl;
            return true;
        }

        if (!decimal.TryParse(raw, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        mgDl = Math.Round(value * factor, 1);
        return mgDl is > 0 and <= MaxMgDl;
    }
}
