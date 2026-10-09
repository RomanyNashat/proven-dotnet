using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace SkillSamples.Secrets;

public sealed class SettablePassword(string value) : IDatabasePassword
{
    public string Value { get; set; } = value;

    public ValueTask<string> GetAsync(CancellationToken ct) => ValueTask.FromResult(Value);
}

public sealed class SettableSecret(string value) : IDatabaseSecret
{
    public string ConnectionString { get; set; } = value;
}

public sealed class PostgresRotationTests : IAsyncLifetime
{
    private static readonly string Server =
        Environment.GetEnvironmentVariable("PG_URL") ?? "Host=localhost;Username=postgres;Password=postgres;Database=postgres";

    private readonly string _role = $"rot_{Guid.NewGuid():N}";

    public Task InitializeAsync() => AdminAsync($"CREATE ROLE {_role} LOGIN PASSWORD 'first-Pass-1'");

    public Task DisposeAsync() => AdminAsync($"DROP ROLE IF EXISTS {_role}");

    [Fact]
    public async Task PasswordRotated_NewConnectionsUseTheNewPassword()
    {
        var password = new SettablePassword("first-Pass-1");
        // Pooling off so every open is a new physical connection; with pooling, open ones keep working.
        var withoutPassword = new NpgsqlConnectionStringBuilder(Server) { Username = _role, Password = null, Pooling = false }.ConnectionString;
        await using var dataSource = RotatingPostgres.Build(withoutPassword, password, refresh: TimeSpan.FromMilliseconds(200));
        Assert.Equal(_role, await CurrentUserAsync(dataSource));

        await AdminAsync($"ALTER ROLE {_role} PASSWORD 'second-Pass-2'");
        password.Value = "second-Pass-2";
        await Task.Delay(TimeSpan.FromSeconds(1));   // longer than the refresh interval

        Assert.Equal(_role, await CurrentUserAsync(dataSource));
    }

    private static async Task<string?> CurrentUserAsync(NpgsqlDataSource dataSource)
    {
        await using var command = dataSource.CreateCommand("SELECT current_user");
        return (string?)await command.ExecuteScalarAsync();
    }

    private static async Task AdminAsync(string sql)
    {
        await using var admin = new NpgsqlConnection(Server);
        await admin.OpenAsync();
#pragma warning disable CA2100 // DDL takes no parameters; the role name is "rot_" + a generated GUID
        await using var command = new NpgsqlCommand(sql, admin);
#pragma warning restore CA2100
        await command.ExecuteNonQueryAsync();
    }
}

public sealed class SqlServerRotationTests : IAsyncLifetime
{
    // TrustServerCertificate only because the CI container has a self-signed certificate.
    private static readonly string Server =
        Environment.GetEnvironmentVariable("MSSQL_URL")
        ?? "Server=localhost,1433;User Id=sa;Password=Samples-2026!;Encrypt=True;TrustServerCertificate=True";

    private readonly string _login = $"rot_{Guid.NewGuid():N}";

    public Task InitializeAsync() => AdminAsync($"CREATE LOGIN [{_login}] WITH PASSWORD = 'first-Pass-1', CHECK_POLICY = OFF");

    public async Task DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        await AdminAsync($"DROP LOGIN [{_login}]");
    }

    private string ConnectionString(string password) =>
        new SqlConnectionStringBuilder(Server) { UserID = _login, Password = password, InitialCatalog = "master" }.ConnectionString;

    [Fact]
    public async Task PasswordRotated_NextRequestUsesTheNewPassword()
    {
        var secret = new SettableSecret(ConnectionString("first-Pass-1"));
        await using var sp = new ServiceCollection().AddSingleton<IDatabaseSecret>(secret).AddReportsDb().BuildServiceProvider();
        Assert.Equal(_login, await CurrentLoginAsync(sp));

        await AdminAsync($"ALTER LOGIN [{_login}] WITH PASSWORD = 'second-Pass-2'");
        secret.ConnectionString = ConnectionString("second-Pass-2");
        SqlConnection.ClearAllPools();   // so an already-open connection can't hide the result

        Assert.Equal(_login, await CurrentLoginAsync(sp));
    }

    [Fact]
    public async Task PooledContext_KeepsTheFirstPassword_AndFailsAfterRotation()
    {
        var secret = new SettableSecret(ConnectionString("first-Pass-1"));
        await using var sp = new ServiceCollection()
            .AddSingleton<IDatabaseSecret>(secret)
            .AddDbContextPool<ReportsDbContext>((s, options) => options.UseSqlServer(s.GetRequiredService<IDatabaseSecret>().ConnectionString))
            .BuildServiceProvider();
        Assert.Equal(_login, await CurrentLoginAsync(sp));

        await AdminAsync($"ALTER LOGIN [{_login}] WITH PASSWORD = 'second-Pass-2'");
        secret.ConnectionString = ConnectionString("second-Pass-2");
        SqlConnection.ClearAllPools();

        var error = await Assert.ThrowsAsync<SqlException>(() => CurrentLoginAsync(sp));
        Assert.Equal(18456, error.Number);   // login failed: the pool's options still carry the old password
    }

    private static async Task<string> CurrentLoginAsync(ServiceProvider sp)
    {
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReportsDbContext>();
        return await db.Database.SqlQueryRaw<string>("SELECT SUSER_SNAME() AS Value").SingleAsync();
    }

    private static async Task AdminAsync(string sql)
    {
        await using var admin = new SqlConnection(Server);
        await admin.OpenAsync();
#pragma warning disable CA2100 // DDL takes no parameters; the login name is "rot_" + a generated GUID
        await using var command = new SqlCommand(sql, admin);
#pragma warning restore CA2100
        await command.ExecuteNonQueryAsync();
    }
}
