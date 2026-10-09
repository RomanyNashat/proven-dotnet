using System.Data;
using Microsoft.Data.SqlClient;

namespace SkillSamples.MsSql;

public sealed record AuditEvent(int UserId, string Action, DateTimeOffset CreatedAt);

public static class AuditBulkCopy
{
    /// <summary>SqlBulkCopy streams the rows in batches; the identity column fills itself.</summary>
    public static async Task InsertAsync(string connectionString, IEnumerable<AuditEvent> events, CancellationToken ct)
    {
        using var table = new DataTable();
        table.Columns.Add("user_id", typeof(int));
        table.Columns.Add("action", typeof(string));
        table.Columns.Add("created_at", typeof(DateTime));
        foreach (var e in events)
        {
            table.Rows.Add(e.UserId, e.Action, e.CreatedAt.UtcDateTime);   // datetime2(3) holds UTC
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        using var bulkCopy = new SqlBulkCopy(connection) { DestinationTableName = "audit_events", BatchSize = 5000 };
        foreach (DataColumn column in table.Columns)
        {
            bulkCopy.ColumnMappings.Add(column.ColumnName, column.ColumnName);   // by name, not position
        }

        await bulkCopy.WriteToServerAsync(table, ct);
    }
}
