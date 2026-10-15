using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using SkillSamples.Production;
using Xunit;

namespace SkillSamples.EfCore;

/// <summary>What dotnet-upgrade says about EF Core 10, on both engines.</summary>
public abstract class UpgradeTests<TFixture>(TFixture db) where TFixture : VisitsFixture
{
    protected TFixture Db => db;

    protected async Task<int[]> SeedAsync(int patientId, params string[] notes)
    {
        await using var context = db.NewContext(patientId);
        var visits = notes.Select(n => new Visit(patientId, n)).ToList();
        context.Visits.AddRange(visits);
        await context.SaveChangesAsync();
        return [.. visits.Select(v => v.Id)];
    }

    [Fact]
    public async Task ExecuteUpdate_ConditionalSetter_OnlySetsTheNoteWhenOneIsGiven()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 15, 9, 0, 0, TimeSpan.Zero));
        var ids = await SeedAsync(901, "first", "second");

        await using (var context = db.NewContext(901))
        {
            Assert.Equal(1, await VisitReview.MarkReviewedAsync(context, [ids[0]], note: "reviewed", time, default));
            Assert.Equal(1, await VisitReview.MarkReviewedAsync(context, [ids[1]], note: null, time, default));
        }

        await using var check = db.NewContext(901);
        var visits = await check.Visits.OrderBy(v => v.Id).ToListAsync();
        Assert.Equal(new[] { "reviewed", "second" }, visits.Select(v => v.Notes).ToArray());
        Assert.All(visits, v => Assert.Equal((901, time.GetUtcNow()), (v.UpdatedBy!.Value, v.UpdatedAt!.Value)));
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_UpgradeDay_TheListFilterFromARequestStillTranslates_AndNoLongerUsesOpenJson()
    {
        // Given: a service moved to .NET 10, where C# 14 binds array.Contains to the span overload, and a
        // request that asks for three of a patient's visits by id
        var ids = await SeedAsync(902, "a", "b", "c", "d");
        int[] requested = [ids[0], ids[2], ids[3]];

        // When: the query filters by the list, as it did on .NET 8
        await using var context = db.NewContext(902);
        var query = context.Visits.Where(v => requested.Contains(v.Id)).OrderBy(v => v.Id);
        var sql = query.ToQueryString();
        var found = await query.Select(v => v.Notes).ToListAsync();

        // Then: EF Core 10 translates it (EF 8 and 9 under C# 14 can't: upgrade EF in the same change), and on
        // SQL Server the list is no longer sent as JSON, so the database's compatibility level stops mattering
        Assert.Equal(new[] { "a", "c", "d" }, found.ToArray());
        Assert.DoesNotContain("OPENJSON", sql, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class PostgresUpgradeTests(PostgresVisits db) : UpgradeTests<PostgresVisits>(db), IClassFixture<PostgresVisits>
{
    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Production)]
    public async Task Production_NoIcuNoTzdata_TheBulkUpdateRunsOnTheUpgradedStack()
    {
        ProductionConditions.Require();
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 15, 9, 0, 0, TimeSpan.Zero));
        var ids = await SeedAsync(903, "before");

        await using (var context = Db.NewContext(903))
        {
            Assert.Equal(1, await VisitReview.MarkReviewedAsync(context, ids, "تمت المراجعة", time, default));
        }

        await using var check = Db.NewContext(903);
        var visit = await check.Visits.SingleAsync();
        Assert.Equal(("تمت المراجعة", time.GetUtcNow()), (visit.Notes, visit.UpdatedAt!.Value));
    }
}

public sealed class SqlServerUpgradeTests(SqlServerVisits db) : UpgradeTests<SqlServerVisits>(db), IClassFixture<SqlServerVisits>;
