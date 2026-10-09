using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;

namespace SkillSamples.MsSql;

/// <summary>One fresh SQL Server database per test class, on the engine CI starts.</summary>
public sealed class SqlDatabase : IAsyncLifetime
{
    // TrustServerCertificate only because the CI container has a self-signed certificate.
    private static readonly string Server =
        Environment.GetEnvironmentVariable("MSSQL_URL")
        ?? "Server=localhost,1433;User Id=sa;Password=Samples-2026!;Encrypt=True;TrustServerCertificate=True";

    public string Name { get; } = $"samples_{Guid.NewGuid():N}";

    public string ConnectionString => new SqlConnectionStringBuilder(Server) { InitialCatalog = Name }.ConnectionString;

    public async Task InitializeAsync()
    {
        await using var admin = new SqlConnection(Server);
        await admin.ExecuteAsync($"CREATE DATABASE [{Name}]");
    }

    public async Task DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        await using var admin = new SqlConnection(Server);
        await admin.ExecuteAsync($"ALTER DATABASE [{Name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{Name}]");
    }

    public async Task<SqlConnection> OpenAsync()
    {
        var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    public async Task ExecuteAsync(string sql, object? args = null)
    {
        await using var connection = await OpenAsync();
        await connection.ExecuteAsync(sql, args);
    }

    public async Task<T> ScalarAsync<T>(string sql, object? args = null)
    {
        await using var connection = await OpenAsync();
        return await connection.ExecuteScalarAsync<T>(sql, args) ?? throw new InvalidOperationException("No value.");
    }
}
