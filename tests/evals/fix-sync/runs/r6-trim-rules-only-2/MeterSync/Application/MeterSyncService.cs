using MeterSync.Domain;
using MeterSync.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace MeterSync.Application;

/// <summary>Pulls new readings from the vendor for every linked meter.</summary>
public sealed class MeterSyncService(SyncDbContext db, MeterVendorClient vendor, TimeProvider time, ILogger<MeterSyncService> logger)
{
    private const int MaxVendorIdLength = 64;

    /// <summary>Runs one sync over every linked meter. Safe to run on several pods at once.</summary>
    /// <param name="ct">Cancels the run.</param>
    /// <returns>How many readings this run stored.</returns>
    public async Task<int> SyncOnceAsync(CancellationToken ct = default)
    {
        var meters = await db.Meters.AsNoTracking()
            .OrderBy(m => m.Id)
            .Select(m => new { m.Id, m.PatientId, m.SerialNumber })
            .ToListAsync(ct);
        var added = 0;
        foreach (var meter in meters)
        {
            IReadOnlyList<VendorReading> readings;
            try
            {
                readings = await vendor.ReadingsAsync(meter.SerialNumber, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException
                                           || (ex is OperationCanceledException && !ct.IsCancellationRequested))
            {
                // One meter the vendor can't serve must not stop the others from syncing.
                logger.LogWarning(ex, "Could not fetch readings for meter {MeterId}", meter.Id);
                continue;
            }

            added += await StoreNewAsync(meter.Id, meter.PatientId, readings, ct);
        }

        return added;
    }

    private async Task<int> StoreNewAsync(int meterId, int patientId, IReadOnlyList<VendorReading> readings, CancellationToken ct)
    {
        var ids = readings
            .Select(r => r.Id)
            .OfType<string>()
            .Where(id => !string.IsNullOrWhiteSpace(id) && id.Length <= MaxVendorIdLength)
            .ToHashSet();
        var known = (await db.Readings.Where(r => ids.Contains(r.VendorId)).Select(r => r.VendorId).ToListAsync(ct)).ToHashSet();
        var now = time.GetUtcNow();
        var added = 0;
        var seen = new HashSet<string>();
        foreach (var r in readings)
        {
            if (r.Id is null || !ids.Contains(r.Id) || known.Contains(r.Id) || !seen.Add(r.Id))
            {
                continue;
            }

            var value = GlucoseValue.TryParse(r.Value, r.Unit);
            if (value is null)
            {
                // The value itself is health data, so it stays out of the log.
                logger.LogWarning("Skipping reading {VendorId} of meter {MeterId}: value or unit not recognised", r.Id, meterId);
                continue;
            }

            var reading = Reading.FromMeter(patientId, meterId, r.Id, value, r.TakenAt.ToUniversalTime(), now);
            added += await InsertIfNewAsync(reading, ct);
        }

        return added;
    }

    // Another pod may be syncing the same meter right now. ON CONFLICT makes the insert idempotent instead of
    // failing on the unique VendorId index.
    private Task<int> InsertIfNewAsync(Reading r, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO readings ("PatientId", "MeterId", "VendorId", "Raw", "Unit", "ValueMgDl", "TakenAt", "Alert", "AlertState")
            VALUES ({r.PatientId}, {r.MeterId}, {r.VendorId}, {r.Raw}, {r.Unit}, {MgDlParameter(r.ValueMgDl)}, {r.TakenAt}, {r.Alert.ToString()}, {r.AlertState.ToString()})
            ON CONFLICT ("VendorId") DO NOTHING
            """,
            ct);

    // Typed, so a null (HI or LO) goes to PostgreSQL as a numeric null rather than an untyped one.
    private static NpgsqlParameter MgDlParameter(decimal? value) =>
        new("mg_dl", NpgsqlDbType.Numeric) { Value = value is null ? DBNull.Value : value.Value };
}
