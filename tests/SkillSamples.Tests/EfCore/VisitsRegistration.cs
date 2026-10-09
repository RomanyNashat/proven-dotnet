using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace SkillSamples.EfCore;

public interface ICaller
{
    int Id { get; }
}

public static class VisitsRegistration
{
    /// <summary>
    /// A pooled context is reused across requests, so it can't take per-request services in its
    /// constructor. Rent it from the pooled factory and set the caller on every rental. Inject the
    /// context, never the factory: a rental that skips this keeps the previous request's caller.
    /// </summary>
    public static IServiceCollection AddVisitsDb(this IServiceCollection services, Action<DbContextOptionsBuilder> useDatabase)
    {
        // useDatabase: o => o.UseNpgsql(cs, pg => pg.EnableRetryOnFailure(3))
        //          or  o => o.UseSqlServer(cs, sql => sql.EnableRetryOnFailure(3))
        services.AddPooledDbContextFactory<VisitsDbContext>(
            (sp, options) =>
            {
                useDatabase(options);
                options.AddInterceptors(new AuditInterceptor(sp.GetRequiredService<TimeProvider>()));
            },
            poolSize: 128);

        services.AddScoped(sp =>
        {
            var db = sp.GetRequiredService<IDbContextFactory<VisitsDbContext>>().CreateDbContext();
            db.CallerId = sp.GetRequiredService<ICaller>().Id;
            return db;   // disposed with the scope, which returns it to the pool
        });

        return services;
    }
}
