using MeterSync.Application;
using MeterSync.Domain;
using Microsoft.EntityFrameworkCore;

namespace MeterSync.Infrastructure;

public sealed class EfSyncStore(SyncDbContext db) : ISyncStore
{
    public async Task<IReadOnlyList<Meter>> MetersAsync(CancellationToken ct) =>
        await db.Meters.AsNoTracking().OrderBy(m => m.Id).ToListAsync(ct);

    public Task<int> AddNewAsync(IReadOnlyCollection<Reading> readings, CancellationToken ct) =>
        db.Database.ExecuteSqlRawAsync(ReadingInsert.Sql(db.Model), ReadingInsert.Parameters(readings), ct);
}
