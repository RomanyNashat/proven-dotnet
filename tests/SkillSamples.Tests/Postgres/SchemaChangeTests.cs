using Npgsql;
using Xunit;

namespace SkillSamples.Postgres;

/// <summary>§5 of postgresql-patterns: which ALTERs rewrite the table, and where CONCURRENTLY can't run.</summary>
public sealed class SchemaChangeTests(PgDatabase db) : IClassFixture<PgDatabase>
{
    private async Task<string> NewTableAsync()
    {
        var table = $"t_{Guid.NewGuid():N}";
        await db.ExecuteAsync($"CREATE TABLE {table} (id int GENERATED ALWAYS AS IDENTITY PRIMARY KEY, code varchar(20) NOT NULL)");
        await db.ExecuteAsync($"INSERT INTO {table} (code) SELECT 'code-' || g FROM generate_series(1, 1000) g");
        return table;
    }

    [Theory]
    [InlineData("int NOT NULL DEFAULT 0")]
    [InlineData("varchar(20) NOT NULL DEFAULT 'new'")]
    [InlineData("timestamptz NOT NULL DEFAULT now()")]   // now() is fixed for the statement: one value for every row
    public async Task AddColumn_WithANonVolatileDefault_DoesNotRewrite(string column)
    {
        var table = await NewTableAsync();
        var before = await db.FileOfAsync(table);

        await db.ExecuteAsync($"ALTER TABLE {table} ADD COLUMN added {column}");

        Assert.Equal(before, await db.FileOfAsync(table));
    }

    [Theory]
    [InlineData("timestamptz NOT NULL DEFAULT clock_timestamp()")]
    [InlineData("double precision NOT NULL DEFAULT random()")]
    public async Task AddColumn_WithAVolatileDefault_RewritesTheTable(string column)
    {
        var table = await NewTableAsync();
        var before = await db.FileOfAsync(table);

        await db.ExecuteAsync($"ALTER TABLE {table} ADD COLUMN added {column}");

        Assert.NotEqual(before, await db.FileOfAsync(table));
    }

    [Fact]
    public async Task WideningVarchar_DoesNotRewrite()
    {
        var table = await NewTableAsync();
        var before = await db.FileOfAsync(table);

        await db.ExecuteAsync($"ALTER TABLE {table} ALTER COLUMN code TYPE varchar(50)");

        Assert.Equal(before, await db.FileOfAsync(table));
    }

    [Fact]
    public async Task NarrowingVarchar_RewritesTheTable_AndFailsOnLongerValues()
    {
        var table = await NewTableAsync();
        var before = await db.FileOfAsync(table);

        await db.ExecuteAsync($"ALTER TABLE {table} ALTER COLUMN code TYPE varchar(9)");   // 'code-1000' is 9 long
        Assert.NotEqual(before, await db.FileOfAsync(table));

        var error = await Assert.ThrowsAsync<PostgresException>(() => db.ExecuteAsync($"ALTER TABLE {table} ALTER COLUMN code TYPE varchar(5)"));
        Assert.Equal(PostgresErrorCodes.StringDataRightTruncation, error.SqlState);
    }

    [Fact]
    public async Task CreateIndexConcurrently_InsideADoBlock_IsRejected()
    {
        var table = await NewTableAsync();

        // What `dotnet ef migrations script --idempotent` emits for IsCreatedConcurrently (efcore.pg #3921).
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.ExecuteAsync(
            $"DO $$ BEGIN CREATE INDEX CONCURRENTLY ix_{table}_code ON {table} (code); END $$"));

        Assert.Contains("cannot be executed from a function", error.MessageText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateIndexConcurrently_InsideATransaction_IsRejected()
    {
        var table = await NewTableAsync();
        await using var connection = await db.DataSource.OpenConnectionAsync();
        await using var tx = await connection.BeginTransactionAsync();
#pragma warning disable CA2100 // DDL; the table name is "t_" + a generated GUID, never input
        await using var command = new NpgsqlCommand($"CREATE INDEX CONCURRENTLY ix_{table}_code ON {table} (code)", connection, tx);
#pragma warning restore CA2100

        var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());

        Assert.Equal(PostgresErrorCodes.ActiveSqlTransaction, error.SqlState);
    }

    [Fact]
    public async Task CreateIndexConcurrently_OnItsOwn_Works()
    {
        var table = await NewTableAsync();

        await db.ExecuteAsync($"CREATE INDEX CONCURRENTLY ix_{table}_code ON {table} (code)");

        Assert.Equal(1, await db.ScalarAsync<int>($"SELECT count(*)::int FROM pg_indexes WHERE indexname = 'ix_{table}_code'"));
    }
}
