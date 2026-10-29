using Microsoft.EntityFrameworkCore;

namespace LabResults.Infrastructure;

public static class LabDbRegistration
{
    /// <summary>
    /// A pooled context is reused across requests, so it can't take the current user in its constructor or
    /// its interceptors. Rent it from the pooled factory and set the caller on every rental. Inject
    /// <see cref="LabDbContext"/>, never the factory: a rental that skips this keeps no caller.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection.</returns>
    public static IServiceCollection AddLabDb(this IServiceCollection services)
    {
        services.AddSingleton<AuditInterceptor>();
        services.AddPooledDbContextFactory<LabDbContext>((sp, options) => options
            .UseNpgsql(sp.GetRequiredService<IConfiguration>().GetConnectionString("Lab"))
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
