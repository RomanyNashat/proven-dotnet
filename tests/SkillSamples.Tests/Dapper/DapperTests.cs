using System.Diagnostics;
using Dapper;
using Xunit;

namespace SkillSamples.DapperReads;

/// <summary>The same checks on both engines.</summary>
public abstract class DapperTests<TEngine>(TEngine db) where TEngine : EngineFixture
{
    protected TEngine Db => db;

    private static NewOrder[] Orders(int customerId, int count) =>
        [.. Enumerable.Range(1, count).Select(i => new NewOrder(customerId, i))];

    [Fact]
    public async Task InsertMany_5000Rows_OneCommand()
    {
        var inserted = await db.Reads().InsertManyAsync(Orders(1, 5000), CancellationToken.None);

        Assert.Equal(5000, inserted);
    }

    [Fact]
    public async Task Keyset_ThreePages_NoGapsNoRepeats()
    {
        await db.Reads().InsertManyAsync(Orders(2, 25), CancellationToken.None);
        var seen = new List<int>();
        var after = 0;
        IReadOnlyList<OrderRow> page;
        do
        {
            page = await db.Reads().PageAsync(customerId: 2, after, size: 10, CancellationToken.None);
            seen.AddRange(page.Select(r => r.Id));
            after = page.Count > 0 ? page[^1].Id : after;
        }
        while (page.Count == 10);

        Assert.Equal(25, seen.Count);
        Assert.Equal(seen.Order(), seen);
        Assert.Equal(25, seen.Distinct().Count());
    }

    [Fact]
    public async Task ByIds_3000Ids_OneParameter()
    {
        await db.Reads().InsertManyAsync(Orders(3, 3000), CancellationToken.None);
        var ids = (await db.Reads().PageAsync(customerId: 3, afterId: 0, size: 3000, CancellationToken.None)).Select(r => r.Id).ToList();

        var rows = await db.Reads().ByIdsAsync(ids, CancellationToken.None);

        Assert.Equal(3000, rows.Count);
    }

    [Fact]
    public async Task Sort_UnknownColumn_RefusedAndNothingRuns()
    {
        await db.Reads().InsertManyAsync(Orders(4, 3), CancellationToken.None);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            db.Reads().TopAsync(customerId: 4, "total; DROP TABLE orders; --", 10, CancellationToken.None));
        var top = await db.Reads().TopAsync(customerId: 4, "TOTAL", 10, CancellationToken.None);

        Assert.Equal(new[] { 3m, 2m, 1m }, top.Select(r => r.Total).ToArray());
    }

    [Fact]
    public async Task CancelledToken_StopsTheQueryInTheDatabase()
    {
        var sleep = db.Engine == Engine.PostgreSql ? "SELECT pg_sleep(20)" : "WAITFOR DELAY '00:00:20'";
        await using var connection = await db.OpenAsync(CancellationToken.None);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var clock = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<Exception>(() =>
            connection.ExecuteAsync(new CommandDefinition(sleep, cancellationToken: cts.Token)));

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"took {clock.Elapsed}");
    }
}

public sealed class PostgresDapperTests(PostgresEngine db) : DapperTests<PostgresEngine>(db), IClassFixture<PostgresEngine>;

public sealed class SqlServerDapperTests(SqlServerEngine db) : DapperTests<SqlServerEngine>(db), IClassFixture<SqlServerEngine>
{
    [Fact]
    public async Task InListExpansion_3000Ids_RefusedBySqlServer()
    {
        await using var connection = await Db.OpenAsync(CancellationToken.None);
        var ids = Enumerable.Range(1, 3000).ToArray();

        var error = await Assert.ThrowsAnyAsync<Exception>(() =>
            connection.QueryAsync<int>("SELECT id FROM orders WHERE id IN @ids", new { ids }));

        Assert.Contains("2100", error.Message);
    }
}
