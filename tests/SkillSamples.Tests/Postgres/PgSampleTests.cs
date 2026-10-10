using Dapper;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using SkillSamples.Production;
using Xunit;

namespace SkillSamples.Postgres;

/// <summary>The code postgresql-patterns shows, run against PostgreSQL.</summary>
public sealed class PgSampleTests(PgDatabase db) : IClassFixture<PgDatabase>
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 8, 0, 0, TimeSpan.Zero);

    private PgOrdersDbContext Orders() =>
        new(new DbContextOptionsBuilder<PgOrdersDbContext>().UseNpgsql(db.ConnectionString).Options);

    private static async Task CreateSchemaAsync(DbContext context)
    {
        await context.Database.ExecuteSqlRawAsync(context.Database.GenerateCreateScript());
    }

    [Fact]
    public async Task OrderConfiguration_GivesTheHouseColumnTypes_AndRoundTrips()
    {
        await using var context = Orders();
        await CreateSchemaAsync(context);
        var order = context.Model.FindEntityType(typeof(PgOrder))!;

        Assert.Equal(NpgsqlValueGenerationStrategy.IdentityAlwaysColumn, NpgsqlPropertyExtensions.GetValueGenerationStrategy(order.FindProperty(nameof(PgOrder.Id))!));
        Assert.Equal("character varying(30)", order.FindProperty(nameof(PgOrder.Reference))!.GetColumnType());
        Assert.Equal("character varying(20)", order.FindProperty(nameof(PgOrder.Status))!.GetColumnType());
        Assert.Equal("numeric(18,2)", order.FindProperty(nameof(PgOrder.Total))!.GetColumnType());

        context.Orders.Add(new PgOrder { Reference = "ORD-1", Status = OrderStatus.Paid, Total = 12.5m, CreatedAt = Now, Tags = ["urgent"] });
        await context.SaveChangesAsync();
        var stored = await db.ScalarAsync<string>("SELECT status FROM orders WHERE reference = 'ORD-1'");
        Assert.Equal("Paid", stored);
    }

    [Fact]
    public async Task ArrayContains_UsesAnyWithoutAGinIndex_AndContainmentWithOne()
    {
        await using var plain = Orders();
        await using var withGin = new PgOrdersWithGinDbContext(
            new DbContextOptionsBuilder<PgOrdersWithGinDbContext>().UseNpgsql(db.ConnectionString).Options);

        var plainSql = plain.Orders.Where(o => o.Tags.Contains("urgent")).ToQueryString();
        var ginSql = withGin.Orders.Where(o => o.Tags.Contains("urgent")).ToQueryString();

        Assert.Contains("= ANY (", plainSql, StringComparison.Ordinal);
        Assert.Contains("@>", ginSql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FullTextSearch_SimpleConfiguration_FindsArabicWords()
    {
        await using var context = new PgProductsDbContext(
            new DbContextOptionsBuilder<PgProductsDbContext>().UseNpgsql(db.ConnectionString).Options);
        await CreateSchemaAsync(context);
        context.Products.AddRange(
            new Product { Name = "مستشفى المدينة", Description = "قسم الطوارئ" },
            new Product { Name = "Al Noor Clinic", Description = "dental" });
        await context.SaveChangesAsync();

        Assert.Equal("مستشفى المدينة", Assert.Single(await context.SearchAsync("الطوارئ", CancellationToken.None)));
        Assert.Equal("Al Noor Clinic", Assert.Single(await context.SearchAsync("dental", CancellationToken.None)));
    }

    [Fact]
    public async Task Upsert_TenAtOnceForOneKey_LeavesOneRow()
    {
        await db.ExecuteAsync("""
            CREATE TABLE user_preferences (
                user_id int NOT NULL, preference_key varchar(50) NOT NULL, preference_value varchar(200) NOT NULL,
                updated_at timestamptz NOT NULL, PRIMARY KEY (user_id, preference_key))
            """);

        await Task.WhenAll(Enumerable.Range(1, 10).Select(async i =>
        {
            await using var connection = await db.DataSource.OpenConnectionAsync();
            await Preferences.SaveAsync(connection, 7, "language", $"value-{i}", Now, CancellationToken.None);
        }));

        Assert.Equal(1, await db.ScalarAsync<int>("SELECT count(*)::int FROM user_preferences WHERE user_id = 7"));
    }

    [Fact]
    public async Task UpsertMany_InsertsNewRows_AndUpdatesExisting()
    {
        await db.ExecuteAsync("""
            CREATE TABLE product_inventory (
                product_id int NOT NULL, warehouse_id int NOT NULL, quantity int NOT NULL, PRIMARY KEY (product_id, warehouse_id))
            """);
        await using var connection = await db.DataSource.OpenConnectionAsync();

        await Preferences.SaveStockAsync(connection, [1, 2], [10, 10], [5, 6], CancellationToken.None);
        await Preferences.SaveStockAsync(connection, [2, 3], [10, 10], [60, 7], CancellationToken.None);

        var rows = (await connection.QueryAsync<(int ProductId, int Quantity)>(
            "SELECT product_id, quantity FROM product_inventory ORDER BY product_id")).ToList();
        Assert.Equal(new[] { (1, 5), (2, 60), (3, 7) }, rows);
    }

    [Fact]
    public async Task Copy_TenThousandRows_OneRoundTrip_IdentityFills()
    {
        await db.ExecuteAsync("""
            CREATE TABLE audit_events (
                id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY, user_id int NOT NULL, action varchar(50) NOT NULL,
                entity_type varchar(50) NOT NULL, entity_id int NOT NULL, created_at timestamptz NOT NULL)
            """);
        var events = Enumerable.Range(1, 10_000).Select(i => new AuditEvent(i % 50, "viewed", "visit", i, Now));

        var written = await AuditImport.ImportAsync(db.DataSource, events, CancellationToken.None);

        Assert.Equal(10_000UL, written);
        Assert.Equal(10_000, await db.ScalarAsync<int>("SELECT count(DISTINCT id)::int FROM audit_events"));
    }

    [Fact]
    public async Task Partitions_InsertWithNoPartition_Fails_AndThePrimaryKeyMustHoldThePartitionKey()
    {
        var withoutKey = await Assert.ThrowsAsync<PostgresException>(() => db.ExecuteAsync("""
            CREATE TABLE events_wrong (id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY, created_at timestamptz NOT NULL)
            PARTITION BY RANGE (created_at)
            """));
        Assert.Equal(PostgresErrorCodes.FeatureNotSupported, withoutKey.SqlState);

        await db.ExecuteAsync("""
            CREATE TABLE events (id bigint GENERATED ALWAYS AS IDENTITY, created_at timestamptz NOT NULL, PRIMARY KEY (id, created_at))
            PARTITION BY RANGE (created_at);
            CREATE TABLE events_2026_10 PARTITION OF events FOR VALUES FROM ('2026-10-01') TO ('2026-11-01');
            """);
        await db.ExecuteAsync("INSERT INTO events (created_at) VALUES ('2026-10-09 08:00:00+00')");

        var noPartition = await Assert.ThrowsAsync<PostgresException>(() => db.ExecuteAsync("INSERT INTO events (created_at) VALUES ('2026-11-02 08:00:00+00')"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, noPartition.SqlState);   // "no partition of relation found for row"
    }

    [Fact]
    public async Task RunExclusive_SecondCallerIsTurnedAway_UntilTheFirstCommits()
    {
        var runner = new ExclusiveRunner(db.DataSource);
        var inside = new TaskCompletionSource();
        var release = new TaskCompletionSource();

        var first = runner.RunExclusiveAsync(42, async (_, _) => { inside.SetResult(); await release.Task; }, CancellationToken.None);
        await inside.Task;

        Assert.False(await runner.RunExclusiveAsync(42, (_, _) => Task.CompletedTask, CancellationToken.None));
        release.SetResult();
        Assert.True(await first);
        Assert.True(await runner.RunExclusiveAsync(42, (_, _) => Task.CompletedTask, CancellationToken.None));
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_ThePodRunningTheNightlyJobCrashes_TheLockGoesWithIt_AndTheNextPodRunsIt()
    {
        var runner = new ExclusiveRunner(db.DataSource);
        await db.ExecuteAsync("CREATE TABLE nightly_runs (pod varchar(10) NOT NULL)");

        // Given: pod A takes the nightly run and dies halfway through
        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunExclusiveAsync(7, async (connection, tx) =>
        {
            await connection.ExecuteAsync("INSERT INTO nightly_runs VALUES ('a')", transaction: tx);
            throw new InvalidOperationException("pod A killed");
        }, CancellationToken.None));

        // When: pod B tries next, and pod C starts while B is still running
        var inside = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var podB = runner.RunExclusiveAsync(7, async (connection, tx) =>
        {
            await connection.ExecuteAsync("INSERT INTO nightly_runs VALUES ('b')", transaction: tx);
            inside.SetResult();
            await release.Task;
        }, CancellationToken.None);
        await inside.Task;
        var podC = await runner.RunExclusiveAsync(7, (_, _) => throw new InvalidOperationException("C must not run"), CancellationToken.None);
        release.SetResult();

        // Then: A's lock and its half-done work are gone, B runs it once, C is turned away
        Assert.True(await podB);
        Assert.False(podC);
        Assert.Equal("b", await db.ScalarAsync<string>("SELECT string_agg(pod, ',') FROM nightly_runs"));
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Production)]
    public async Task Production_OnAPodInUtc_DateTimeNowLooksLikeUtc_ButIsStillRefused()
    {
        ProductionConditions.Require();
        await db.ExecuteAsync("CREATE TABLE seen (at timestamptz NOT NULL)");

        // On the pod, local time is UTC: the same clock reading, but Kind=Local, as DateTime.Now gives.
        var local = TimeProvider.System.GetLocalNow().LocalDateTime;
        Assert.Equal(DateTimeKind.Local, local.Kind);
        Assert.Equal(TimeSpan.Zero, TimeZoneInfo.Local.GetUtcOffset(local));

        await using var connection = await db.DataSource.OpenConnectionAsync();
        var refused = await Assert.ThrowsAnyAsync<Exception>(() => Insert(local));
        await Insert(local.ToUniversalTime());

        // Npgsql checks the Kind, not the offset, so code that "worked on the pod" is refused the same way
        // on every machine, rather than storing the wrong instant on a developer's laptop in UTC+3.
        Assert.Contains("Kind=Local", refused.Message, StringComparison.Ordinal);
        Assert.Equal(1, await db.ScalarAsync<int>("SELECT count(*)::int FROM seen"));

        async Task Insert(DateTime at)
        {
            await using var command = new NpgsqlCommand("INSERT INTO seen VALUES (@at)", connection);
            command.Parameters.Add(new NpgsqlParameter("at", NpgsqlTypes.NpgsqlDbType.TimestampTz) { Value = at });
            await command.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task SessionLock_StaysHeldAfterTheConnectionGoesBackToThePool()
    {
        await using (var first = await db.DataSource.OpenConnectionAsync())
        {
            await first.ExecuteAsync("SELECT pg_advisory_lock(77)");
        }   // back to the pool, still connected to the server, still holding the lock

        await using var second = await db.DataSource.OpenConnectionAsync();
        Assert.False(await second.ExecuteScalarAsync<bool>("SELECT pg_try_advisory_lock(77)"));
        Assert.False(await second.ExecuteScalarAsync<bool>("SELECT pg_advisory_unlock(77)"));   // another connection can't release it
    }

    [Fact]
    public async Task DiagnosticQueries_Run()
    {
        await using var connection = await db.DataSource.OpenConnectionAsync();

        await connection.QueryAsync("""
            SELECT pid, now() - query_start AS duration, state, query FROM pg_stat_activity
            WHERE state <> 'idle' AND now() - query_start > interval '5 seconds' ORDER BY duration DESC
            """);
        await connection.QueryAsync("SELECT relname, n_dead_tup, n_live_tup FROM pg_stat_user_tables WHERE n_dead_tup > 10000 ORDER BY n_dead_tup DESC");
        await connection.QueryAsync("""
            SELECT indexrelname, idx_scan, pg_size_pretty(pg_relation_size(indexrelid)) AS size
            FROM pg_stat_user_indexes WHERE idx_scan = 0 ORDER BY pg_relation_size(indexrelid) DESC
            """);
    }
}
