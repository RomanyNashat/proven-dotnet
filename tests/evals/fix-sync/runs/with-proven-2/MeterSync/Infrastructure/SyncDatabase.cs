using System.Data.Common;
using Npgsql;

namespace MeterSync.Infrastructure;

public static class SyncDatabase
{
    /// <summary>
    /// Connections per pod when the connection string doesn't say. Npgsql's default is 100, and PostgreSQL
    /// allows 100 connections in all by default, so a few pods at the default exhaust the server. 20 is a
    /// starting point: agree the real number with the DBA (pods x this must stay under max_connections).
    /// </summary>
    public const int DefaultMaxPoolSize = 20;

    private static readonly string[] PoolSizeKeys = ["maximumpoolsize", "maxpoolsize"];

    /// <summary>Caps the pool at <see cref="DefaultMaxPoolSize"/> unless the connection string sets a size.</summary>
    /// <param name="connectionString">The configured connection string.</param>
    /// <returns>The connection string to use.</returns>
    public static string WithPoolLimit(string connectionString)
    {
        var configured = new DbConnectionStringBuilder { ConnectionString = connectionString };
        var hasPoolSize = configured.Keys.Cast<string>()
            .Any(key => PoolSizeKeys.Contains(key.Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant()));
        return hasPoolSize
            ? connectionString
            : new NpgsqlConnectionStringBuilder(connectionString) { MaxPoolSize = DefaultMaxPoolSize }.ConnectionString;
    }
}
