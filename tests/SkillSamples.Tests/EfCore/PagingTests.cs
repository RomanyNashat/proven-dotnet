using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using SkillSamples.Production;
using Xunit;

namespace SkillSamples.EfCore;

/// <summary>Keyset paging over rows that share a sort key, on both engines.</summary>
public abstract class PagingTests<TFixture>(TFixture db) where TFixture : VisitsFixture
{
    private const int PageSize = 2;

    /// <summary>Visits saved at the same instant: an import, or a batch the clinic books at once.</summary>
    private async Task<int[]> SeedSameInstantAsync(int patientId, int count)
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 15, 9, 0, 0, TimeSpan.Zero));
        await using var context = db.NewContext(patientId, time);
        var visits = Enumerable.Range(1, count).Select(i => new Visit(patientId, $"visit {i}")).ToList();
        context.Visits.AddRange(visits);
        await context.SaveChangesAsync();
        return [.. visits.Select(v => v.Id).Order()];
    }

    private async Task<List<int>> ReadAllPagesAsync(int patientId, Func<VisitsDbContext, VisitRow?, Task<List<VisitRow>>> nextPage)
    {
        var seen = new List<int>();
        VisitRow? last = null;
        for (var pages = 0; pages < 10; pages++)
        {
            await using var context = db.NewContext(patientId);
            var page = await nextPage(context, last);
            if (page.Count == 0) break;
            seen.AddRange(page.Select(r => r.Id));
            last = page[^1];
        }

        return seen;
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_FiveVisitsSavedInOneInstant_PagingByTimeAloneLosesThree_TheIdTieBreakerReturnsAll()
    {
        // Given: a patient whose five visits were imported in one save, so they share a timestamp
        var ids = await SeedSameInstantAsync(patientId: 921, count: 5);

        // When: the app pages through them two at a time, with a cursor on the timestamp alone
        var byTimeOnly = await ReadAllPagesAsync(921, (context, last) =>
        {
            var visits = context.Visits.AsQueryable();
            if (last is not null)
            {
                visits = visits.Where(v => v.CreatedAt > last.CreatedAt);
            }

            return visits.OrderBy(v => v.CreatedAt).Take(PageSize)
                .Select(v => new VisitRow(v.Id, v.CreatedAt, v.Notes))
                .ToListAsync();
        });

        // Then: the second page asks for "later than the first page's last row" and finds nothing: three
        // visits never reach the screen, and nothing fails
        Assert.Equal(PageSize, byTimeOnly.Count);

        // When: it pages with the id as tie-breaker, in the order and in the cursor
        var withTieBreaker = await ReadAllPagesAsync(921, (context, last) =>
            VisitPages.NextAsync(context, last is null ? null : new VisitCursor(last.CreatedAt, last.Id), PageSize, default));

        // Then: every visit shows up once, in id order
        Assert.Equal(ids, withTieBreaker.ToArray());
    }

    [Fact]
    public async Task NextAsync_CursorInTheMiddleOfATie_ResumesAfterTheLastIdSeen()
    {
        var ids = await SeedSameInstantAsync(patientId: 922, count: 4);
        await using var context = db.NewContext(922);
        var first = await VisitPages.NextAsync(context, after: null, size: 3, default);

        var rest = await VisitPages.NextAsync(context, new VisitCursor(first[^1].CreatedAt, first[^1].Id), size: 3, default);

        Assert.Equal(ids[3..], rest.Select(r => r.Id).ToArray());
    }
}

public sealed class PostgresPagingTests(PostgresVisits db) : PagingTests<PostgresVisits>(db), IClassFixture<PostgresVisits>;

public sealed class SqlServerPagingTests(SqlServerVisits db) : PagingTests<SqlServerVisits>(db), IClassFixture<SqlServerVisits>;
