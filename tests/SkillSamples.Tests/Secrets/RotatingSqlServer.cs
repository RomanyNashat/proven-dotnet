using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace SkillSamples.Secrets;

/// <summary>The current connection string, kept fresh in the background from the vault.</summary>
public interface IDatabaseSecret
{
    string ConnectionString { get; }
}

public sealed class ReportsDbContext(DbContextOptions<ReportsDbContext> options) : DbContext(options);

public static class RotatingSqlServer
{
    /// <summary>
    /// SqlClient has no password callback. AddDbContext builds the options for every scope, so each request
    /// reads the current connection string; SqlClient pools by connection string, so the new password gets
    /// a new pool and the old pool's connections drain on their own. AddDbContextPool would build the
    /// options once and keep the first password.
    /// </summary>
    public static IServiceCollection AddReportsDb(this IServiceCollection services) =>
        services.AddDbContext<ReportsDbContext>((sp, options) =>
            options.UseSqlServer(sp.GetRequiredService<IDatabaseSecret>().ConnectionString));
}
