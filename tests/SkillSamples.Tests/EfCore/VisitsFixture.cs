using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Xunit;

namespace SkillSamples.EfCore;

/// <summary>One fresh database per test class, on a real engine started by CI.</summary>
public abstract class VisitsFixture : IAsyncLifetime
{
    protected readonly string Database = $"samples_{Guid.NewGuid():N}";

    public abstract Engine Engine { get; }

    /// <summary>What the engine's retrying strategy treats as transient (a dropped connection).</summary>
    public abstract Exception TransientError();

    public abstract void UseDatabase(DbContextOptionsBuilder options, bool retry = false);

    public VisitsDbContext NewContext(int callerId, TimeProvider? time = null, bool retry = false, IInterceptor? extra = null)
    {
        var options = new DbContextOptionsBuilder<VisitsDbContext>();
        UseDatabase(options, retry);
        options.AddInterceptors(Interceptors(time, extra));
        return new VisitsDbContext(options.Options) { CallerId = callerId };
    }

    public LeakyVisitsDbContext NewLeakyContext(int callerId)
    {
        var options = new DbContextOptionsBuilder<LeakyVisitsDbContext>();
        UseDatabase(options);
        return new LeakyVisitsDbContext(options.Options) { CallerId = callerId };
    }

    public async Task InitializeAsync()
    {
        await CreateDatabaseAsync();

        // Schema from a script generated from the model, as a reviewed migration script would be. No Migrate.
        await using var db = NewContext(callerId: 0);
        foreach (var batch in Regex.Split(db.Database.GenerateCreateScript(), @"^\s*GO\s*$", RegexOptions.Multiline))
        {
            if (!string.IsNullOrWhiteSpace(batch))
            {
                await db.Database.ExecuteSqlRawAsync(batch);
            }
        }
    }

    public abstract Task DisposeAsync();

    protected abstract Task CreateDatabaseAsync();

    private static List<IInterceptor> Interceptors(TimeProvider? time, IInterceptor? extra)
    {
        var interceptors = new List<IInterceptor> { new AuditInterceptor(time ?? TimeProvider.System) };
        if (extra is not null)
        {
            interceptors.Add(extra);
        }

        return interceptors;
    }
}

public sealed class PostgresVisits : VisitsFixture
{
    private static readonly string Server =
        Environment.GetEnvironmentVariable("PG_URL") ?? "Host=localhost;Username=postgres;Password=postgres;Database=postgres";

    public override Engine Engine => Engine.PostgreSql;

    private string ConnectionString => new NpgsqlConnectionStringBuilder(Server) { Database = Database }.ConnectionString;

    public override Exception TransientError() => new NpgsqlException("Simulated dropped connection", new TimeoutException());

    public override void UseDatabase(DbContextOptionsBuilder options, bool retry = false) =>
        options.UseNpgsql(ConnectionString, npgsql =>
        {
            if (retry)
            {
                npgsql.EnableRetryOnFailure(maxRetryCount: 3);
            }
        });

    protected override async Task CreateDatabaseAsync()
    {
        await using var admin = new NpgsqlConnection(Server);
        await admin.OpenAsync();
#pragma warning disable CA2100 // DDL takes no parameters; the name is "samples_" + a generated GUID, never input
        await using var create = new NpgsqlCommand($"CREATE DATABASE {Database}", admin);
#pragma warning restore CA2100
        await create.ExecuteNonQueryAsync();
    }

    public override async Task DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(Server);
        await admin.OpenAsync();
#pragma warning disable CA2100 // DDL takes no parameters; the name is "samples_" + a generated GUID, never input
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS {Database} WITH (FORCE)", admin);
#pragma warning restore CA2100
        await drop.ExecuteNonQueryAsync();
    }
}

public sealed class SqlServerVisits : VisitsFixture
{
    // TrustServerCertificate only because the CI container has a self-signed certificate.
    private static readonly string Server =
        Environment.GetEnvironmentVariable("MSSQL_URL")
        ?? "Server=localhost,1433;User Id=sa;Password=Samples-2026!;Encrypt=True;TrustServerCertificate=True";

    public override Engine Engine => Engine.SqlServer;

    private string ConnectionString => new SqlConnectionStringBuilder(Server) { InitialCatalog = Database }.ConnectionString;

    public override Exception TransientError() => new TimeoutException("Simulated dropped connection");

    public override void UseDatabase(DbContextOptionsBuilder options, bool retry = false) =>
        options.UseSqlServer(ConnectionString, sql =>
        {
            if (retry)
            {
                sql.EnableRetryOnFailure(maxRetryCount: 3);
            }
        });

    protected override async Task CreateDatabaseAsync()
    {
        await using var admin = new SqlConnection(Server);
        await admin.OpenAsync();
#pragma warning disable CA2100 // DDL takes no parameters; the name is "samples_" + a generated GUID, never input
        await using var create = new SqlCommand($"CREATE DATABASE [{Database}]", admin);
#pragma warning restore CA2100
        await create.ExecuteNonQueryAsync();
    }

    public override async Task DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        await using var admin = new SqlConnection(Server);
        await admin.OpenAsync();
#pragma warning disable CA2100 // DDL takes no parameters; the name is "samples_" + a generated GUID, never input
        await using var drop = new SqlCommand($"ALTER DATABASE [{Database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{Database}]", admin);
#pragma warning restore CA2100
        await drop.ExecuteNonQueryAsync();
    }
}
