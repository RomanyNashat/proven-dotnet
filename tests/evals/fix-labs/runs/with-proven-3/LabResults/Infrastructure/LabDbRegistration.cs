using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LabResults.Infrastructure;

public static class LabDbRegistration
{
    /// <summary>
    /// A pooled context with the caller set on every rental. Inject <see cref="LabDbContext"/>, never the
    /// factory: a context rented from the factory directly keeps the previous request's caller.
    /// </summary>
    /// <param name="services">The services.</param>
    /// <returns>The same services.</returns>
    public static IServiceCollection AddLabDb(this IServiceCollection services)
    {
        services.AddOptions<LabDbOptions>()
            .Configure<IConfiguration>((o, config) => o.ConnectionString = config.GetConnectionString("Lab"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddScoped<CurrentUser>();
        services.AddSingleton<AuditInterceptor>();
        services.AddPooledDbContextFactory<LabDbContext>((sp, o) => o
            .UseNpgsql(
                sp.GetRequiredService<IOptions<LabDbOptions>>().Value.ConnectionString,
                // Safe here: the service opens no transactions of its own (postgresql-patterns §3).
                pg => pg.EnableRetryOnFailure(3))
            .AddInterceptors(sp.GetRequiredService<AuditInterceptor>()));

        services.AddScoped(sp =>
        {
            var db = sp.GetRequiredService<IDbContextFactory<LabDbContext>>().CreateDbContext();
            db.CallerId = sp.GetRequiredService<CurrentUser>().Id;
            return db;   // disposed with the scope, which returns it to the pool
        });

        return services;
    }
}
