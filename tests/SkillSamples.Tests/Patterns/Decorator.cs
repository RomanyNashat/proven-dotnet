using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace SkillSamples.Patterns;

public sealed record ClinicInfo(int Id, string NameEn, string NameAr);

public interface IClinicDirectory
{
    Task<ClinicInfo?> FindAsync(int clinicId, CancellationToken ct);
}

// Adds caching without touching the real directory or its callers. A miss (null) is cached too, so an
// unknown id doesn't reach the database on every request.
public sealed class CachedClinicDirectory(IClinicDirectory inner, IMemoryCache cache) : IClinicDirectory
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

    public Task<ClinicInfo?> FindAsync(int clinicId, CancellationToken ct) =>
        cache.GetOrCreateAsync(("clinic", clinicId), entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = Ttl;
            return inner.FindAsync(clinicId, ct);
        });
}

public static class ClinicDirectoryRegistration
{
    // The real directory is registered as itself; the interface resolves to the decorator wrapping it.
    // No Scrutor needed for one decorator.
    public static IServiceCollection AddClinicDirectory<TDirectory>(this IServiceCollection services)
        where TDirectory : class, IClinicDirectory
    {
        services.AddMemoryCache();
        services.AddScoped<TDirectory>();
        services.AddScoped<IClinicDirectory>(sp => new CachedClinicDirectory(
            sp.GetRequiredService<TDirectory>(), sp.GetRequiredService<IMemoryCache>()));
        return services;
    }
}
