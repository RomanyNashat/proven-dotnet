using Dapper;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace SkillSamples.Postgres;

/// <summary>§2 of postgresql-patterns: what behaves differently for someone coming from SQL Server.</summary>
public sealed class SqlServerHabitTests(PgDatabase db) : IClassFixture<PgDatabase>, IAsyncLifetime
{
    private readonly string _people = $"people_{Guid.NewGuid():N}";

    public async Task InitializeAsync()
    {
        await db.ExecuteAsync($"""
            CREATE TABLE {_people} (
                id int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                email varchar(100) NOT NULL,
                manager_id int NULL,
                seen_at timestamptz NULL);
            INSERT INTO {_people} (email, manager_id) VALUES ('Ali@Example.com', NULL), ('sara@example.com', 1);
            """);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Equality_IsCaseSensitive_IlikeIsNot()
    {
        Assert.Equal(0, await db.ScalarAsync<int>($"SELECT count(*)::int FROM {_people} WHERE email = @e", new { e = "ali@example.com" }));
        Assert.Equal(1, await db.ScalarAsync<int>($"SELECT count(*)::int FROM {_people} WHERE email ILIKE @e", new { e = "ali@example.com" }));
    }

    [Fact]
    public async Task DateTimeOffset_NotUtc_IsRefused()
    {
        await using var connection = await db.DataSource.OpenConnectionAsync();
        var riyadhNoon = new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.FromHours(3));

        var error = await Assert.ThrowsAnyAsync<Exception>(() =>
            connection.ExecuteAsync($"UPDATE {_people} SET seen_at = @at WHERE id = 1", new { at = riyadhNoon }));

        Assert.Contains("only offset 0 (UTC) is supported", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DateTime_NotUtc_TypedAsTimestamptz_IsRefused()
    {
        await using var connection = await db.DataSource.OpenConnectionAsync();
#pragma warning disable CA2100 // the table name is "people_" + a generated GUID, never input
        await using var command = new NpgsqlCommand($"UPDATE {_people} SET seen_at = @at WHERE id = 1", connection);
#pragma warning restore CA2100
        command.Parameters.Add(new NpgsqlParameter("at", NpgsqlDbType.TimestampTz) { Value = new DateTime(2026, 10, 9, 12, 0, 0) });

        var error = await Assert.ThrowsAnyAsync<Exception>(() => command.ExecuteNonQueryAsync());

        Assert.Contains("Kind=Unspecified", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DateTime_NotUtc_AsAPlainParameter_IsShiftedByTheSessionTimeZone()
    {
        // No error on this path: Npgsql sends an Unspecified DateTime as `timestamp`, and the server reads it
        // in the session's time zone. Noon "Riyadh" lands as 09:00 UTC; with another TimeZone setting it
        // lands somewhere else. EF types the parameter as timestamptz and throws instead (above).
        await using var connection = await db.DataSource.OpenConnectionAsync();
        await connection.ExecuteAsync("SET TIME ZONE 'Asia/Riyadh'");
        await connection.ExecuteAsync($"UPDATE {_people} SET seen_at = @at WHERE id = 1", new { at = new DateTime(2026, 10, 9, 12, 0, 0) });

        var stored = await connection.ExecuteScalarAsync<DateTime>($"SELECT seen_at FROM {_people} WHERE id = 1");

        Assert.Equal(new DateTime(2026, 10, 9, 9, 0, 0, DateTimeKind.Utc), stored);
    }

    [Fact]
    public async Task OneErrorInATransaction_AbortsEverythingAfterIt()
    {
        await using var connection = await db.DataSource.OpenConnectionAsync();
        await using var tx = await connection.BeginTransactionAsync();
        await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync("SELECT 1 / 0", transaction: tx));

        var error = await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync("SELECT 1", transaction: tx));

        Assert.Equal(PostgresErrorCodes.InFailedSqlTransaction, error.SqlState);   // 25P02
    }

    [Fact]
    public async Task AnyWithAnIntArray_MatchesTheList()
    {
        var ids = new[] { 1, 2, 999 };

        Assert.Equal(2, await db.ScalarAsync<int>($"SELECT count(*)::int FROM {_people} WHERE id = ANY(@ids)", new { ids }));
    }

    [Fact]
    public async Task NotIn_WithANullInTheSubquery_ReturnsNothing_NotExistsWorks()
    {
        // manager_id holds 1 and NULL. People who manage nobody: id 2.
        var notIn = await db.ScalarAsync<int>($"SELECT count(*)::int FROM {_people} p WHERE p.id NOT IN (SELECT manager_id FROM {_people})");
        var notExists = await db.ScalarAsync<int>($"SELECT count(*)::int FROM {_people} p WHERE NOT EXISTS (SELECT 1 FROM {_people} m WHERE m.manager_id = p.id)");

        Assert.Equal(0, notIn);
        Assert.Equal(1, notExists);
    }

    [Fact]
    public async Task Between_IncludesTheEndInstant()
    {
        await db.ExecuteAsync($"UPDATE {_people} SET seen_at = '2026-10-10 00:00:00+00' WHERE id = 2");

        var between = await db.ScalarAsync<int>($"SELECT count(*)::int FROM {_people} WHERE seen_at BETWEEN '2026-10-09 00:00:00+00' AND '2026-10-10 00:00:00+00'");
        var halfOpen = await db.ScalarAsync<int>($"SELECT count(*)::int FROM {_people} WHERE seen_at >= '2026-10-09 00:00:00+00' AND seen_at < '2026-10-10 00:00:00+00'");

        Assert.Equal(1, between);    // midnight counted in the 9th, and again in the 10th
        Assert.Equal(0, halfOpen);
    }
}
