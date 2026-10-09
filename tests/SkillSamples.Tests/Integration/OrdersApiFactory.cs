using System.Data.Common;
using System.Text.RegularExpressions;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Respawn;
using SkillSamples.OrdersApi;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;
using Xunit;

namespace SkillSamples.Integration;

/// <summary>
/// Starts the real service against a real database in a container. A service has one engine and keeps
/// one subclass; both are here so CI checks both.
/// </summary>
public abstract class OrdersApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private Respawner _respawner = null!;

    protected abstract DockerContainer Container { get; }

    protected abstract string Engine { get; }

    protected abstract string ConnectionString { get; }

    protected abstract DbConnection NewConnection();

    protected abstract RespawnerOptions ResetOptions { get; }

    public async Task InitializeAsync()
    {
        await Container.StartAsync();

        // Build the schema from SQL, never Migrate() (rules/efcore-rules.md). This generates it from the
        // model; a service with committed migration scripts runs those files in order instead, which
        // tests the scripts the pipeline will run.
        using (var scope = Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            foreach (var batch in Regex.Split(db.Database.GenerateCreateScript(), @"^\s*GO\s*$", RegexOptions.Multiline))
            {
                if (!string.IsNullOrWhiteSpace(batch))
                {
                    await db.Database.ExecuteSqlRawAsync(batch);
                }
            }
        }

        await using var connection = NewConnection();
        await connection.OpenAsync();
        _respawner = await Respawner.CreateAsync(connection, ResetOptions);
    }

    /// <summary>Empties the tables between tests: one container per class, clean data per test.</summary>
    public async Task ResetDatabaseAsync()
    {
        await using var connection = NewConnection();
        await connection.OpenAsync();
        await _respawner.ResetAsync(connection);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        await Container.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Configuration, not service surgery: the app reads these when it builds the DbContext. Removing
        // DbContextOptions<T> and calling AddDbContext again leaves the app's provider registered on EF 9+.
        builder.UseSetting("ConnectionStrings:Orders", ConnectionString);
        builder.UseSetting("Database:Engine", Engine);
        builder.ConfigureTestServices(services =>
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, null));
    }
}

public sealed class PostgresOrdersApi : OrdersApiFactory
{
    // The major version production runs (rules: pin the image, never "latest").
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();

    protected override DockerContainer Container => _postgres;

    protected override string Engine => "PostgreSql";

    protected override string ConnectionString => _postgres.GetConnectionString();

    protected override DbConnection NewConnection() => new NpgsqlConnection(ConnectionString);

    protected override RespawnerOptions ResetOptions => new()
    {
        DbAdapter = DbAdapter.Postgres,
        SchemasToInclude = ["public"],
    };
}

public sealed class SqlServerOrdersApi : OrdersApiFactory
{
    // 2022 is the major version; Developer edition is free for development and testing.
    private readonly MsSqlContainer _sqlServer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    protected override DockerContainer Container => _sqlServer;

    protected override string Engine => "SqlServer";

    protected override string ConnectionString => _sqlServer.GetConnectionString();

    protected override DbConnection NewConnection() => new SqlConnection(ConnectionString);

    protected override RespawnerOptions ResetOptions => new()
    {
        DbAdapter = DbAdapter.SqlServer,
        SchemasToInclude = ["dbo"],
    };
}
