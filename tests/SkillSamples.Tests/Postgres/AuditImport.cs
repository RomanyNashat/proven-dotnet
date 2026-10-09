using Npgsql;
using NpgsqlTypes;

namespace SkillSamples.Postgres;

public sealed record AuditEvent(int UserId, string Action, string EntityType, int EntityId, DateTimeOffset CreatedAt);

public static class AuditImport
{
    /// <summary>COPY in binary: one round trip for the whole set. The identity column fills itself.</summary>
    public static async Task<ulong> ImportAsync(NpgsqlDataSource dataSource, IEnumerable<AuditEvent> events, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var writer = await connection.BeginBinaryImportAsync(
            "COPY audit_events (user_id, action, entity_type, entity_id, created_at) FROM STDIN (FORMAT BINARY)", ct);

        foreach (var e in events)
        {
            await writer.StartRowAsync(ct);
            await writer.WriteAsync(e.UserId, NpgsqlDbType.Integer, ct);
            await writer.WriteAsync(e.Action, NpgsqlDbType.Varchar, ct);
            await writer.WriteAsync(e.EntityType, NpgsqlDbType.Varchar, ct);
            await writer.WriteAsync(e.EntityId, NpgsqlDbType.Integer, ct);
            await writer.WriteAsync(e.CreatedAt, NpgsqlDbType.TimestampTz, ct);   // offset 0
        }

        return await writer.CompleteAsync(ct);   // nothing is saved without this
    }
}
