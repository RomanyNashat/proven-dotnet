using MeterSync.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;
using NpgsqlTypes;

namespace MeterSync.Infrastructure;

/// <summary>
/// One statement per batch: the readings arrive as one array per column, and a reading another pod already
/// stored (same vendor id) is skipped by the unique index instead of failing the batch.
/// </summary>
public static class ReadingInsert
{
    private static readonly string[] Columns =
    [
        nameof(Reading.PatientId), nameof(Reading.MeterId), nameof(Reading.VendorId), nameof(Reading.Raw),
        nameof(Reading.Unit), nameof(Reading.ValueMgDl), nameof(Reading.TakenAt), nameof(Reading.Alert),
        nameof(Reading.Status), nameof(Reading.AlertAttempts),
    ];

    /// <summary>The statement, with table and column names read from the model so the two can't drift.</summary>
    public static string Sql(IModel model)
    {
        var entity = model.FindEntityType(typeof(Reading))!;
        var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        string Column(string property) => $"\"{entity.FindProperty(property)!.GetColumnName(table)}\"";

        var columns = string.Join(", ", Columns.Select(Column));
        var parameters = string.Join(", ", Columns.Select((_, i) => $"@p{i}"));
        return $"INSERT INTO {entity.GetTableName()} ({columns}) SELECT * FROM unnest({parameters}) ON CONFLICT ({Column(nameof(Reading.VendorId))}) DO NOTHING";
    }

    public static NpgsqlParameter[] Parameters(IReadOnlyCollection<Reading> readings) =>
    [
        Array("p0", NpgsqlDbType.Integer, readings.Select(r => r.PatientId).ToArray()),
        Array("p1", NpgsqlDbType.Integer, readings.Select(r => r.MeterId).ToArray()),
        Array("p2", NpgsqlDbType.Varchar, readings.Select(r => r.VendorId).ToArray()),
        Array("p3", NpgsqlDbType.Varchar, readings.Select(r => r.Raw).ToArray()),
        Array("p4", NpgsqlDbType.Varchar, readings.Select(r => r.Unit).ToArray()),
        Array("p5", NpgsqlDbType.Numeric, readings.Select(r => r.ValueMgDl).ToArray()),
        Array("p6", NpgsqlDbType.TimestampTz, readings.Select(r => r.TakenAt.UtcDateTime).ToArray()),
        Array("p7", NpgsqlDbType.Varchar, readings.Select(r => r.Alert.ToString()).ToArray()),
        Array("p8", NpgsqlDbType.Varchar, readings.Select(r => r.Status.ToString()).ToArray()),
        Array("p9", NpgsqlDbType.Integer, readings.Select(r => r.AlertAttempts).ToArray()),
    ];

    private static NpgsqlParameter Array<T>(string name, NpgsqlDbType elementType, T[] values) =>
        new(name, NpgsqlDbType.Array | elementType) { Value = values };
}
