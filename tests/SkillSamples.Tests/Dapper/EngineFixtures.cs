using System.Data.Common;
using Dapper;
using Microsoft.Data.SqlClient;
using Npgsql;
using Xunit;

namespace SkillSamples.DapperReads;

/// <summary>One fresh database per test class, on a real engine started by CI.</summary>
public abstract class EngineFixture : IAsyncLifetime
{
    protected readonly string Database = $"samples_{Guid.NewGuid():N}";

    public abstract Engine Engine { get; }

    public abstract Task<DbConnection> OpenAsync(CancellationToken ct);

    public OrderReads Reads() => new(OpenAsync, Engine);

    public abstract Task InitializeAsync();

    public abstract Task DisposeAsync();
}

public sealed class PostgresEngine : EngineFixture
{
    private static readonly string Server =
        Environment.GetEnvironmentVariable("PG_URL") ?? "Host=localhost;Username=postgres;Password=postgres;Database=postgres";

    public override Engine Engine => Engine.PostgreSql;

    public override async Task<DbConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(Server) { Database = Database }.ConnectionString);
        await connection.OpenAsync(ct);
        return connection;
    }

    public override async Task InitializeAsync()
    {
        await using (var admin = new NpgsqlConnection(Server))
        {
            await admin.ExecuteAsync($"CREATE DATABASE {Database}");
        }

        await using var db = await OpenAsync(CancellationToken.None);
        await db.ExecuteAsync("""
            CREATE TABLE orders (
                id int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                customer_id int NOT NULL,
                total numeric(18,2) NOT NULL,
                created_at timestamptz NOT NULL DEFAULT now());
            CREATE INDEX ix_orders_customer_id ON orders (customer_id, id);
            """);
    }

    public override async Task DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(Server);
        await admin.ExecuteAsync($"DROP DATABASE IF EXISTS {Database} WITH (FORCE)");
    }
}

public sealed class SqlServerEngine : EngineFixture
{
    // TrustServerCertificate only because the CI container has a self-signed certificate.
    private static readonly string Server =
        Environment.GetEnvironmentVariable("MSSQL_URL")
        ?? "Server=localhost,1433;User Id=sa;Password=Samples-2026!;Encrypt=True;TrustServerCertificate=True";

    public override Engine Engine => Engine.SqlServer;

    public override async Task<DbConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new SqlConnection(new SqlConnectionStringBuilder(Server) { InitialCatalog = Database }.ConnectionString);
        await connection.OpenAsync(ct);
        return connection;
    }

    public override async Task InitializeAsync()
    {
        await using (var admin = new SqlConnection(Server))
        {
            await admin.ExecuteAsync($"CREATE DATABASE [{Database}]");
        }

        await using var db = await OpenAsync(CancellationToken.None);
        await db.ExecuteAsync("""
            CREATE TABLE orders (
                id int IDENTITY PRIMARY KEY,
                customer_id int NOT NULL,
                total decimal(18,2) NOT NULL,
                created_at datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME());
            CREATE INDEX ix_orders_customer_id ON orders (customer_id, id);
            """);
    }

    public override async Task DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        await using var admin = new SqlConnection(Server);
        await admin.ExecuteAsync($"ALTER DATABASE [{Database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{Database}]");
    }
}
