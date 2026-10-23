using MeterSync.Domain;
using MeterSync.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace MeterSync.Application;

/// <summary>Pulls new readings from the vendor for every linked meter.</summary>
public sealed class MeterSyncService(SyncDbContext db, MeterVendorClient vendor, ILogger<MeterSyncService> logger)
{
    // One statement per meter. ON CONFLICT makes it safe for two pods syncing the same meter at once, and
    // for readings already stored: the vendor returns the meter's whole history every time.
    private const string InsertReadings = """
        INSERT INTO readings ("PatientId", "MeterId", "VendorId", "Raw", "Unit", "ValueMgDl", "TakenAt", "Alert", "AlertDelivery", "AlertAttempts")
        SELECT @patientId, @meterId, r.vendor_id, r.raw, r.unit, r.value_mg_dl, r.taken_at, r.alert, r.alert_delivery, 0
        FROM unnest(@vendorIds, @raws, @units, @valuesMgDl, @takenAts, @alerts, @deliveries)
            AS r (vendor_id, raw, unit, value_mg_dl, taken_at, alert, alert_delivery)
        ON CONFLICT ("VendorId") DO NOTHING
        """;

    /// <summary>Fetches and stores each meter's readings. One meter failing doesn't stop the others.</summary>
    /// <returns>How many new readings were stored.</returns>
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
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // The type only: a vendor or database message can carry the serial or reading values.
                logger.LogError("Sync of meter {MeterId} failed: {ErrorType}", meter.Id, ex.GetType().Name);
            }
        }

        return added;
    }

    private async Task<int> SyncMeterAsync(Meter meter, CancellationToken ct)
    {
        var fetched = await vendor.ReadingsAsync(meter.SerialNumber, ct);
        var readings = new List<Reading>(fetched.Count);
        foreach (var r in fetched.DistinctBy(r => r.Id))
        {
            if (Reading.FromMeter(meter.PatientId, meter.Id, r.Id ?? "", r.Value ?? "", r.Unit ?? "", r.TakenAt, meter.LinkedAt) is { } reading)
            {
                readings.Add(reading);
            }
            else
            {
                // No value or unit in the log: a glucose reading is health data.
                logger.LogWarning("Skipping reading {VendorId} of meter {MeterId}: it can't be read", r.Id, meter.Id);
            }
        }

        if (readings.Count == 0)
        {
            return 0;
        }

        return await db.Database.ExecuteSqlRawAsync(
            InsertReadings,
            [
                new NpgsqlParameter("patientId", NpgsqlDbType.Integer) { Value = meter.PatientId },
                new NpgsqlParameter("meterId", NpgsqlDbType.Integer) { Value = meter.Id },
                ArrayParameter("vendorIds", NpgsqlDbType.Varchar, readings.Select(r => r.VendorId)),
                ArrayParameter("raws", NpgsqlDbType.Varchar, readings.Select(r => r.Raw)),
                ArrayParameter("units", NpgsqlDbType.Varchar, readings.Select(r => r.Unit)),
                ArrayParameter("valuesMgDl", NpgsqlDbType.Numeric, readings.Select(r => r.ValueMgDl)),
                ArrayParameter("takenAts", NpgsqlDbType.TimestampTz, readings.Select(r => r.TakenAt)),
                ArrayParameter("alerts", NpgsqlDbType.Varchar, readings.Select(r => r.Alert.ToString())),
                ArrayParameter("deliveries", NpgsqlDbType.Varchar, readings.Select(r => r.AlertDelivery.ToString())),
            ],
            ct);
    }

    private static NpgsqlParameter ArrayParameter<T>(string name, NpgsqlDbType elementType, IEnumerable<T> values) =>
        new(name, NpgsqlDbType.Array | elementType) { Value = values.ToArray() };
}
