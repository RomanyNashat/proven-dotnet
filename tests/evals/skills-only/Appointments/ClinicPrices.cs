using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

namespace Appointments;

public sealed class ClinicPrices(AppointmentsDbContext db, HybridCache cache)
{
    private static string Key(int clinicId) => $"clinic-price:v1:{clinicId}";

    // Read on every booking screen.
    public ValueTask<decimal> PriceAsync(int clinicId, CancellationToken ct) =>
        cache.GetOrCreateAsync(Key(clinicId),
            async token => await db.Clinics.Where(c => c.Id == clinicId).Select(c => c.Price).SingleAsync(token),
            tags: ["clinic-prices"],
            cancellationToken: ct);

    public async Task UpdateAsync(int clinicId, decimal price, CancellationToken ct)
    {
        await db.Clinics.Where(c => c.Id == clinicId).ExecuteUpdateAsync(s => s.SetProperty(c => c.Price, price), ct);
        await cache.RemoveAsync(Key(clinicId), ct);
    }
}
