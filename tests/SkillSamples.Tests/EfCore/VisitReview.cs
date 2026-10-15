using Microsoft.EntityFrameworkCore;

namespace SkillSamples.EfCore;

public static class VisitReview
{
    /// <summary>
    /// EF Core 10 takes a plain lambda here, so a setter can depend on a condition. Before 10 it took an
    /// expression tree: a conditional setter meant one call per combination, or building the tree by hand.
    /// The owner filter still applies, and the audit interceptor still doesn't run (efcore-patterns §6).
    /// </summary>
    public static Task<int> MarkReviewedAsync(
        VisitsDbContext db, IReadOnlyCollection<int> ids, string? note, TimeProvider time, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        return db.Visits
            .Where(v => ids.Contains(v.Id))
            .ExecuteUpdateAsync(set =>
            {
                set.SetProperty(v => v.UpdatedAt, now);
                set.SetProperty(v => v.UpdatedBy, db.CallerId);
                if (note is not null)
                {
                    set.SetProperty(v => v.Notes, note);
                }
            }, ct);
    }
}
