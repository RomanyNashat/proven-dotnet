using Microsoft.EntityFrameworkCore;

namespace SkillSamples.Ddd;

/// <summary>
/// Loads and adds whole aggregates, nothing else. No Update() (the context tracks the changes), no
/// partial loads, no IQueryable out of the repository. Saving is the caller's unit of work.
/// </summary>
public sealed class OrderRepository(OrdersDbContext db)
{
    public Task<Order?> GetAsync(int id, CancellationToken ct) =>
        db.Orders.Include(o => o.Lines).SingleOrDefaultAsync(o => o.Id == id, ct);

    public async Task AddAsync(Order order, CancellationToken ct) =>
        await db.Orders.AddAsync(order, ct);   // AddAsync: HiLo may fetch the next block of ids here
}
