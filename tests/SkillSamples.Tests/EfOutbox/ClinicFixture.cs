using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace SkillSamples.EfOutbox;

/// <summary>One fresh database per test class, on a real engine started by CI.</summary>
public abstract class ClinicFixture : IAsyncLifetime
{
    protected readonly string Database = $"samples_{Guid.NewGuid():N}";

    public ClinicDbContext NewContext(TimeProvider? time = null)
    {
        var options = new DbContextOptionsBuilder<ClinicDbContext>();
        UseDatabase(options);
        options.AddInterceptors(new OutboxInterceptor(time ?? TimeProvider.System));
        return new ClinicDbContext(options.Options);
    }

    public async Task InitializeAsync()
    {
        await CreateDatabaseAsync();

        // The schema goes in as a script generated from the model, the same path a reviewed migration
        // script takes. Nothing calls EnsureCreated or Migrate (a rule here).
        await using var db = NewContext();
        foreach (var batch in Regex.Split(db.Database.GenerateCreateScript(), @"^\s*GO\s*$", RegexOptions.Multiline))
        {
            if (!string.IsNullOrWhiteSpace(batch))
            {
                await db.Database.ExecuteSqlRawAsync(batch);
            }
        }
    }

    public abstract Task DisposeAsync();

    protected abstract void UseDatabase(DbContextOptionsBuilder options);

    protected abstract Task CreateDatabaseAsync();
}

public sealed class PostgresClinic : ClinicFixture
{
    private static readonly string Server =
        Environment.GetEnvironmentVariable("PG_URL") ?? "Host=localhost;Username=postgres;Password=postgres;Database=postgres";

    protected override void UseDatabase(DbContextOptionsBuilder options) =>
        options.UseNpgsql(new NpgsqlConnectionStringBuilder(Server) { Database = Database }.ConnectionString);

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

public sealed class SqlServerClinic : ClinicFixture
{
    // TrustServerCertificate only because the CI container has a self-signed certificate.
    private static readonly string Server =
        Environment.GetEnvironmentVariable("MSSQL_URL")
        ?? "Server=localhost,1433;User Id=sa;Password=Samples-2026!;Encrypt=True;TrustServerCertificate=True";

    protected override void UseDatabase(DbContextOptionsBuilder options) =>
        options.UseSqlServer(new SqlConnectionStringBuilder(Server) { InitialCatalog = Database }.ConnectionString);

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
