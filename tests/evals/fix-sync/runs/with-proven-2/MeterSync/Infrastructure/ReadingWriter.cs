using MeterSync.Domain;
using Microsoft.EntityFrameworkCore;

namespace MeterSync.Infrastructure;

public static class ReadingWriter
{
    /// <summary>
    /// Inserts the readings in one statement and skips any another pod inserted first. SELECT-then-INSERT
    /// races between pods: both insert the same vendor id, one hits the unique index (23505), and its whole
    /// batch rolls back.
    /// </summary>
    /// <param name="db">The context.</param>
    /// <param name="readings">New readings, all from one meter.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>How many were inserted.</returns>
    public static Task<int> InsertNewAsync(this SyncDbContext db, IReadOnlyList<Reading> readings, CancellationToken ct)
    {
        var patientIds = readings.Select(r => r.PatientId).ToArray();
        var meterIds = readings.Select(r => r.MeterId).ToArray();
        var vendorIds = readings.Select(r => r.VendorId).ToArray();
        var raws = readings.Select(r => r.Raw).ToArray();
        var units = readings.Select(r => r.Unit).ToArray();
        var values = readings.Select(r => r.ValueMgDl).ToArray();
        var takenAts = readings.Select(r => r.TakenAt).ToArray();
        var alerts = readings.Select(r => r.Alert.ToString()).ToArray();
        var deliveries = readings.Select(r => r.AlertDelivery.ToString()).ToArray();
        var attempts = readings.Select(r => r.AlertAttempts).ToArray();

        // Every value is a parameter (one array per column; unnest pairs them up by position).
        return db.Database.ExecuteSqlAsync($"""
            INSERT INTO readings ("PatientId", "MeterId", "VendorId", "Raw", "Unit", "ValueMgDl", "TakenAt", "Alert", "AlertDelivery", "AlertAttempts")
            SELECT * FROM unnest({patientIds}, {meterIds}, {vendorIds}, {raws}, {units}, {values}, {takenAts}, {alerts}, {deliveries}, {attempts})
            ON CONFLICT ("VendorId") DO NOTHING
            """, ct);
    }
}
