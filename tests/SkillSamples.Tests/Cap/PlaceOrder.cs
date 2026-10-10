using DotNetCore.CAP;
using Npgsql;

namespace SkillSamples.Cap;

public sealed record OrderPlaced(int OrderId, string PatientName);

public sealed class PlaceOrder(NpgsqlDataSource db, ICapPublisher events)
{
    public const string Topic = "orders.placed";

    // The order row and the event commit together: CAP writes the event to its published table in the
    // same transaction, and sends it to the broker after the commit. A rollback sends nothing.
    public async Task<int> RunAsync(string patientName, bool failBeforeCommit, CancellationToken ct)
    {
        await using var connection = await db.OpenConnectionAsync(ct);
        using var transaction = await connection.BeginTransactionAsync(events, autoCommit: false, ct);
        try
        {
            await using var insert = new NpgsqlCommand(
                "INSERT INTO orders (patient_name) VALUES (@name) RETURNING id", connection, (NpgsqlTransaction)transaction.DbTransaction!);
            insert.Parameters.AddWithValue("name", patientName);
            var id = (int)(await insert.ExecuteScalarAsync(ct))!;

            await events.PublishAsync(Topic, new OrderPlaced(id, patientName), cancellationToken: ct);

            if (failBeforeCommit)
                throw new InvalidOperationException("Something failed after the event was written.");

            await transaction.CommitAsync(ct);
            return id;
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }
}
