using Npgsql;

namespace SkillSamples.Secrets;

/// <summary>A source of the current database password: a vault, a mounted secret file, a token service.</summary>
public interface IDatabasePassword
{
    ValueTask<string> GetAsync(CancellationToken ct);
}

public static class RotatingPostgres
{
    /// <summary>
    /// The connection string carries everything except the password. Npgsql asks for the password again
    /// every <paramref name="refresh"/>, and each new physical connection uses the latest one; connections
    /// already open stay as they are. Register the result as a singleton.
    /// </summary>
    public static NpgsqlDataSource Build(string connectionStringWithoutPassword, IDatabasePassword password, TimeSpan refresh)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionStringWithoutPassword);
        builder.UsePeriodicPasswordProvider(
            (_, ct) => password.GetAsync(ct),
            successRefreshInterval: refresh,
            failureRefreshInterval: TimeSpan.FromSeconds(10));
        return builder.Build();
    }
}
