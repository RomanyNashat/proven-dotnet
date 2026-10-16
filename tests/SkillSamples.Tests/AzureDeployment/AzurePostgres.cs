using Azure.Core;
using Npgsql;

namespace SkillSamples.AzureDeployment;

public static class AzurePostgres
{
    private static readonly string[] Scopes = ["https://ossrdbms-aad.database.windows.net/.default"];

    /// <summary>
    /// Azure Database for PostgreSQL with a managed identity. Npgsql has no built-in Entra login, so the token
    /// is the password, fetched again before it expires (tokens last about an hour). Pass the
    /// <c>DefaultAzureCredential</c>; the connection string has no password.
    /// </summary>
    public static NpgsqlDataSource DataSource(string connectionString, TokenCredential credential, TimeSpan refreshEvery)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        builder.UsePeriodicPasswordProvider(
            async (_, ct) => (await credential.GetTokenAsync(new TokenRequestContext(Scopes), ct)).Token,
            successRefreshInterval: refreshEvery,           // 55 minutes in production
            failureRefreshInterval: TimeSpan.FromSeconds(1));
        return builder.Build();
    }
}
