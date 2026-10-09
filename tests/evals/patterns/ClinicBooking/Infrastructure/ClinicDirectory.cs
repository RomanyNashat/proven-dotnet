using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

namespace ClinicBooking.Infrastructure;

public sealed record ClinicSummary(int Id, string NameAr, string NameEn, string City);

public interface IClinicDirectory
{
    Task<IReadOnlyList<ClinicSummary>> InCityAsync(string city, CancellationToken ct);
}

public sealed class SqlClinicDirectory(ClinicDbContext db) : IClinicDirectory
{
    public async Task<IReadOnlyList<ClinicSummary>> InCityAsync(string city, CancellationToken ct) =>
        await db.Clinics.AsNoTracking()
            .Where(c => c.City == city)
            .Select(c => new ClinicSummary(c.Id, c.NameAr, c.NameEn, c.City))
            .ToListAsync(ct);
}

// The clinic list changes a few times a year and is read on every search screen.
public sealed class CachedClinicDirectory(IClinicDirectory inner, HybridCache cache) : IClinicDirectory
{
    public async Task<IReadOnlyList<ClinicSummary>> InCityAsync(string city, CancellationToken ct) =>
        await cache.GetOrCreateAsync($"clinics:{city}", async token => await inner.InCityAsync(city, token),
            new HybridCacheEntryOptions { Expiration = TimeSpan.FromHours(1) }, cancellationToken: ct);
}
