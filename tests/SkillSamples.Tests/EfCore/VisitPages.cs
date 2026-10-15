using Microsoft.EntityFrameworkCore;

namespace SkillSamples.EfCore;

public sealed record VisitRow(int Id, DateTimeOffset CreatedAt, string Notes);

/// <summary>Where the next page starts: the last row's sort key and its id, which breaks ties.</summary>
public sealed record VisitCursor(DateTimeOffset CreatedAt, int Id);

public static class VisitPages
{
    /// <summary>
    /// Keyset paging, oldest first. The order ends in the id, so it's unique: rows that share a timestamp
    /// keep the same order on every page, and the cursor can resume in the middle of them.
    /// </summary>
    public static Task<List<VisitRow>> NextAsync(VisitsDbContext db, VisitCursor? after, int size, CancellationToken ct)
    {
        var visits = db.Visits.AsQueryable();
        if (after is not null)
        {
            visits = visits.Where(v => v.CreatedAt > after.CreatedAt || (v.CreatedAt == after.CreatedAt && v.Id > after.Id));
        }

        return visits
            .OrderBy(v => v.CreatedAt).ThenBy(v => v.Id)
            .Take(size)
            .Select(v => new VisitRow(v.Id, v.CreatedAt, v.Notes))
            .ToListAsync(ct);
    }
}
