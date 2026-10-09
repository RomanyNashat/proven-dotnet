using Dapper;
using Npgsql;
using Xunit;

namespace SkillSamples.Postgres;

/// <summary>One fresh PostgreSQL database per test class, on the engine CI starts.</summary>
public sealed class PgDatabase : IAsyncLifetime
{
    private static readonly string Server =
        Environment.GetEnvironmentVariable("PG_URL") ?? "Host=localhost;Username=postgres;Password=postgres;Database=postgres";

    private readonly string _name = $"samples_{Guid.NewGuid():N}";

    public string ConnectionString => new NpgsqlConnectionStringBuilder(Server) { Database = _name }.ConnectionString;

    public NpgsqlDataSource DataSource { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await using (var admin = new NpgsqlConnection(Server))
        {
            await admin.ExecuteAsync($"CREATE DATABASE {_name}");
        }

        DataSource = NpgsqlDataSource.Create(ConnectionString);
    }

    public async Task DisposeAsync()
    {
        await DataSource.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(Server);
        await admin.ExecuteAsync($"DROP DATABASE IF EXISTS {_name} WITH (FORCE)");
    }

    public async Task ExecuteAsync(string sql, object? args = null)
    {
        await using var connection = await DataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(sql, args);
    }

    public async Task<T> ScalarAsync<T>(string sql, object? args = null)
    {
        await using var connection = await DataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<T>(sql, args) ?? throw new InvalidOperationException("No value.");
    }

    /// <summary>The table's file on disk. A new one after an ALTER means PostgreSQL rewrote the table.</summary>
    public Task<uint> FileOfAsync(string table) => ScalarAsync<uint>($"SELECT pg_relation_filenode('{table}')::oid");
}
