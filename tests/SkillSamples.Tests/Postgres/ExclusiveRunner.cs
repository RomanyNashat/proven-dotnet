using Dapper;
using Npgsql;

namespace SkillSamples.Postgres;

public sealed class ExclusiveRunner(NpgsqlDataSource dataSource)
{
    /// <summary>
    /// Runs the work only if no one else holds the lock. The lock belongs to the transaction: commit,
    /// rollback, or a dropped connection releases it, so a crashed pod can't keep it.
    /// </summary>
    public async Task<bool> RunExclusiveAsync(long lockKey, Func<NpgsqlConnection, NpgsqlTransaction, Task> work, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var acquired = await connection.ExecuteScalarAsync<bool>(
            new CommandDefinition("SELECT pg_try_advisory_xact_lock(@lockKey)", new { lockKey }, tx, cancellationToken: ct));
        if (!acquired)
        {
            return false;   // someone else holds it
        }

        await work(connection, tx);
        await tx.CommitAsync(ct);   // the lock goes with the commit
        return true;
    }
}
