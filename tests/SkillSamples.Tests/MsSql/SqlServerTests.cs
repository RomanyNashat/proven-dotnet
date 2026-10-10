using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SkillSamples.Production;
using Xunit;

namespace SkillSamples.MsSql;

/// <summary>The claims and code in sqlserver-patterns, run against SQL Server 2022.</summary>
public sealed class SqlServerTests(SqlDatabase db) : IClassFixture<SqlDatabase>
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 8, 0, 0, TimeSpan.Zero);

    private async Task CreateInventoryAsync()
    {
        await db.ExecuteAsync("""
            IF OBJECT_ID('product_inventory') IS NULL
                CREATE TABLE product_inventory (
                    product_id int NOT NULL, warehouse_id int NOT NULL, quantity int NOT NULL,
                    CONSTRAINT pk_product_inventory PRIMARY KEY (product_id, warehouse_id));
            """);
    }

    [Fact]
    public async Task ReadCommitted_WithoutSnapshot_AReaderWaitsForAWriter_WithSnapshotItReadsTheLastCommittedValue()
    {
        await db.ExecuteAsync("CREATE TABLE rc_check (id int PRIMARY KEY, status varchar(20) NOT NULL); INSERT INTO rc_check VALUES (1, 'Pending');");

        await using var writer = await db.OpenAsync();
        await using (var tx = writer.BeginTransaction())
        {
            await writer.ExecuteAsync("UPDATE rc_check SET status = 'Paid' WHERE id = 1", transaction: tx);

            await using var reader = await db.OpenAsync();
            var blocked = await Assert.ThrowsAsync<SqlException>(() =>
                reader.ExecuteScalarAsync<string>(new CommandDefinition("SELECT status FROM rc_check WHERE id = 1", commandTimeout: 2)));
            Assert.Equal(-2, blocked.Number);   // timeout: the reader waited on the writer's lock
            tx.Rollback();
        }

        SqlConnection.ClearAllPools();
        await db.ExecuteAsync($"ALTER DATABASE [{db.Name}] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE");

        await using var writer2 = await db.OpenAsync();
        await using var tx2 = writer2.BeginTransaction();
        await writer2.ExecuteAsync("UPDATE rc_check SET status = 'Paid' WHERE id = 1", transaction: tx2);
        await using var reader2 = await db.OpenAsync();
        var seen = await reader2.ExecuteScalarAsync<string>(new CommandDefinition("SELECT status FROM rc_check WHERE id = 1", commandTimeout: 2));
        Assert.Equal("Pending", seen);   // no wait, and no dirty read
        tx2.Rollback();
    }

    [Fact]
    public async Task Save_TenAtOnceForOneNewKey_OneRowNoErrors()
    {
        await CreateInventoryAsync();

        await Task.WhenAll(Enumerable.Range(1, 10).Select(async i =>
        {
            await using var connection = await db.OpenAsync();
            await Inventory.SaveAsync(connection, productId: 1, warehouseId: 1, quantity: i, CancellationToken.None);
        }));

        Assert.Equal(1, await db.ScalarAsync<int>("SELECT COUNT(*) FROM product_inventory WHERE product_id = 1"));
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_TwoScannersCountANewProductAtOnce_WithoutHintsOneFails_WithThemTheSecondWaitsAndUpdates()
    {
        await CreateInventoryAsync();
        const string update = "UPDATE product_inventory SET quantity = @q WHERE product_id = @p AND warehouse_id = 1; SELECT @@ROWCOUNT;";
        const string insert = "INSERT INTO product_inventory (product_id, warehouse_id, quantity) VALUES (@p, 1, @q)";

        // Given: two scanners record product 30 at the same moment, without lock hints. Both update nothing.
        await using (var scannerA = await db.OpenAsync())
        await using (var scannerB = await db.OpenAsync())
        {
            await using var txA = scannerA.BeginTransaction();
            await using var txB = scannerB.BeginTransaction();
            Assert.Equal(0, await scannerA.ExecuteScalarAsync<int>(update, new { p = 30, q = 5 }, txA));
            Assert.Equal(0, await scannerB.ExecuteScalarAsync<int>(update, new { p = 30, q = 7 }, txB));
            await scannerA.ExecuteAsync(insert, new { p = 30, q = 5 }, txA);
            await txA.CommitAsync();

            // When: the second inserts too. Then: the primary key refuses it, and that count is lost
            var lost = await Assert.ThrowsAsync<SqlException>(() => scannerB.ExecuteAsync(insert, new { p = 30, q = 7 }, txB));
            Assert.Equal(2627, lost.Number);
        }

        // Given: the same with the skill's SaveAsync (UPDLOCK, HOLDLOCK), scanner A still in its transaction
        await using var holder = await db.OpenAsync();
        await using var tx = holder.BeginTransaction();
        await holder.ExecuteAsync("""
            UPDATE product_inventory WITH (UPDLOCK, HOLDLOCK) SET quantity = 5 WHERE product_id = 31 AND warehouse_id = 1;
            INSERT INTO product_inventory (product_id, warehouse_id, quantity) VALUES (31, 1, 5);
            """, transaction: tx);

        // When: scanner B saves while A hasn't committed
        await using var scannerB2 = await db.OpenAsync();
        var second = Inventory.SaveAsync(scannerB2, productId: 31, warehouseId: 1, quantity: 7, CancellationToken.None);
        await Task.Delay(500);
        Assert.False(second.IsCompleted);   // waiting on A's key-range lock, not racing it
        await tx.CommitAsync();
        await second;

        // Then: one row, with B's count
        Assert.Equal(7, await db.ScalarAsync<int>("SELECT quantity FROM product_inventory WHERE product_id = 31"));
    }

    [Fact]
    public async Task MergeWithHoldlock_TenAtOnceForOneNewKey_OneRowNoErrors()
    {
        await CreateInventoryAsync();

        await Task.WhenAll(Enumerable.Range(1, 10).Select(async i =>
        {
            await using var connection = await db.OpenAsync();
            await Inventory.MergeAsync(connection, productId: 2, warehouseId: 1, quantity: i, CancellationToken.None);
        }));

        Assert.Equal(1, await db.ScalarAsync<int>("SELECT COUNT(*) FROM product_inventory WHERE product_id = 2"));
    }

    [Fact]
    public async Task TableValuedParameter_FiltersByTheList()
    {
        await CreateInventoryAsync();
        await db.ExecuteAsync("""
            IF TYPE_ID('dbo.int_list') IS NULL CREATE TYPE dbo.int_list AS TABLE (id int NOT NULL PRIMARY KEY);
            INSERT INTO product_inventory VALUES (100, 1, 5), (101, 1, 0), (102, 1, 3);
            """);
        await using var connection = await db.OpenAsync();

        var inStock = await Inventory.InStockAsync(connection, [100, 101, 102, 103, 100], CancellationToken.None);

        Assert.Equal(new[] { 100, 102 }, inStock.Order());
    }

    [Fact]
    public async Task BulkCopy_TenThousandRows_IdentityFills()
    {
        await db.ExecuteAsync("""
            CREATE TABLE audit_events (
                id bigint IDENTITY PRIMARY KEY, user_id int NOT NULL, action varchar(50) NOT NULL, created_at datetime2(3) NOT NULL)
            """);

        await AuditBulkCopy.InsertAsync(db.ConnectionString, Enumerable.Range(1, 10_000).Select(i => new AuditEvent(i % 50, "viewed", Now)), CancellationToken.None);

        Assert.Equal(10_000, await db.ScalarAsync<int>("SELECT COUNT(DISTINCT id) FROM audit_events"));
    }

    [Fact]
    public async Task Json_BoundedColumnWithIsJsonCheck_RejectsBadJson_AndOpenJsonReadsIt()
    {
        await db.ExecuteAsync("""
            CREATE TABLE tagged_orders (
                id int IDENTITY PRIMARY KEY,
                tags nvarchar(400) NOT NULL CONSTRAINT ck_tagged_orders_tags CHECK (ISJSON(tags) = 1),
                metadata nvarchar(1000) NOT NULL CONSTRAINT ck_tagged_orders_metadata CHECK (ISJSON(metadata) = 1),
                region AS CAST(JSON_VALUE(metadata, '$.region') AS varchar(10)) PERSISTED);
            CREATE INDEX ix_tagged_orders_region ON tagged_orders (region);
            INSERT INTO tagged_orders (tags, metadata) VALUES (N'["priority","express"]', N'{"region":"SA"}'), (N'["normal"]', N'{"region":"AE"}');
            """);

        var badJson = await Assert.ThrowsAsync<SqlException>(() => db.ExecuteAsync("INSERT INTO tagged_orders (tags, metadata) VALUES (N'priority', N'{}')"));
        Assert.Equal(547, badJson.Number);   // CHECK constraint

        Assert.Equal(1, await db.ScalarAsync<int>("SELECT COUNT(*) FROM tagged_orders o CROSS APPLY OPENJSON(o.tags) t WHERE t.value = @tag", new { tag = "priority" }));
        Assert.Equal(1, await db.ScalarAsync<int>("SELECT COUNT(*) FROM tagged_orders WHERE region = @region", new { region = "SA" }));
    }

    [Fact]
    public async Task Temporal_AsOfBeforeTheChange_ReturnsTheOldRow()
    {
        var options = new DbContextOptionsBuilder<TemporalDbContext>().UseSqlServer(db.ConnectionString).Options;
        await using var context = new TemporalDbContext(options);
        foreach (var batch in System.Text.RegularExpressions.Regex.Split(context.Database.GenerateCreateScript(), @"^\s*GO\s*$", System.Text.RegularExpressions.RegexOptions.Multiline))
        {
            if (!string.IsNullOrWhiteSpace(batch))
            {
                await context.Database.ExecuteSqlRawAsync(batch);
            }
        }

        var order = new TemporalOrder { Status = "Pending" };
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        await Task.Delay(50);
        var beforeChange = TimeProvider.System.GetUtcNow().UtcDateTime;   // the period columns are in UTC
        await Task.Delay(50);
        order.Status = "Paid";
        await context.SaveChangesAsync();

        await using var reader = new TemporalDbContext(options);
        Assert.Equal("Pending", (await reader.Orders.TemporalAsOf(beforeChange).SingleAsync(o => o.Id == order.Id)).Status);
        Assert.Equal(2, await reader.Orders.TemporalAll().CountAsync(o => o.Id == order.Id));
    }

    [Fact]
    public async Task RowLevelSecurity_EachTenantSeesAndWritesOnlyItsRows()
    {
        await db.ExecuteAsync("""
            CREATE TABLE records (id int IDENTITY PRIMARY KEY, tenant_id int NOT NULL, note nvarchar(200) NOT NULL);
            """);
        await db.ExecuteAsync("""
            CREATE FUNCTION dbo.fn_tenant_filter(@tenant_id int)
            RETURNS TABLE WITH SCHEMABINDING
            AS RETURN SELECT 1 AS allowed WHERE @tenant_id = CAST(SESSION_CONTEXT(N'tenant_id') AS int);
            """);
        await db.ExecuteAsync("""
            CREATE SECURITY POLICY dbo.tenant_policy
                ADD FILTER PREDICATE dbo.fn_tenant_filter(tenant_id) ON dbo.records,
                ADD BLOCK PREDICATE dbo.fn_tenant_filter(tenant_id) ON dbo.records AFTER INSERT
            WITH (STATE = ON);
            """);

        TenantDbContext For(int tenant) => new(new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlServer(db.ConnectionString).AddInterceptors(new TenantSessionInterceptor()).Options) { TenantId = tenant };

        await using (var one = For(1))
        {
            one.Records.Add(new TenantRecord { TenantId = 1, Note = "tenant one" });
            await one.SaveChangesAsync();
        }

        await using (var two = For(2))
        {
            two.Records.Add(new TenantRecord { TenantId = 2, Note = "tenant two" });
            await two.SaveChangesAsync();
            Assert.Equal("tenant two", Assert.Single(await two.Records.Select(r => r.Note).ToListAsync()));

            two.Records.Add(new TenantRecord { TenantId = 1, Note = "written into tenant one" });
            var blocked = await Assert.ThrowsAsync<DbUpdateException>(() => two.SaveChangesAsync());
            Assert.Equal(33504, ((SqlException)blocked.InnerException!).Number);   // block predicate
        }
    }

    [Fact]
    public async Task SessionContext_IsClearedWhenAPooledConnectionIsReused_AndReadOnlyCantBeChanged()
    {
        await using (var first = await db.OpenAsync())
        {
            await first.ExecuteAsync("EXEC sp_set_session_context @key = N'tenant_id', @value = 7, @read_only = 1;");
            var changed = await Assert.ThrowsAsync<SqlException>(() =>
                first.ExecuteAsync("EXEC sp_set_session_context @key = N'tenant_id', @value = 8;"));
            Assert.Equal(15664, changed.Number);   // read-only key
        }

        await using var reused = await db.OpenAsync();   // same pooled connection, reset by the driver
        Assert.Null(await reused.ExecuteScalarAsync<int?>("SELECT CAST(SESSION_CONTEXT(N'tenant_id') AS int)"));
    }

    [Fact]
    public async Task Indexes_FromTheSkill_Build_IncludingOnlineRebuildAndColumnstore()
    {
        await db.ExecuteAsync("""
            CREATE TABLE idx_orders (
                id int IDENTITY PRIMARY KEY, customer_id int NOT NULL, status varchar(20) NOT NULL,
                total decimal(18,2) NOT NULL, is_deleted bit NOT NULL DEFAULT 0, created_at datetime2(3) NOT NULL);
            CREATE NONCLUSTERED INDEX ix_idx_orders_customer_status ON idx_orders (customer_id, status) INCLUDE (total, created_at);
            CREATE NONCLUSTERED INDEX ix_idx_orders_pending ON idx_orders (created_at DESC) WHERE status = 'Pending' AND is_deleted = 0;
            CREATE NONCLUSTERED COLUMNSTORE INDEX ix_idx_orders_analytics ON idx_orders (customer_id, status, total, created_at);
            """);

        await db.ExecuteAsync("ALTER INDEX ix_idx_orders_customer_status ON idx_orders REBUILD WITH (ONLINE = ON, MAXDOP = 4)");

        Assert.Equal(4, await db.ScalarAsync<int>("SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID('idx_orders')"));
    }

    [Fact]
    public async Task QueryStore_SkillQueriesRun()
    {
        await db.ExecuteAsync($"""
            ALTER DATABASE [{db.Name}] SET QUERY_STORE = ON (
                OPERATION_MODE = READ_WRITE, DATA_FLUSH_INTERVAL_SECONDS = 900, MAX_STORAGE_SIZE_MB = 1024,
                INTERVAL_LENGTH_MINUTES = 60, QUERY_CAPTURE_MODE = AUTO)
            """);
        await using var connection = await db.OpenAsync();

        await connection.QueryAsync("""
            SELECT TOP 10 q.query_id, qt.query_sql_text, rs.avg_duration / 1000.0 AS avg_duration_ms, rs.count_executions
            FROM sys.query_store_query q
            JOIN sys.query_store_query_text qt ON q.query_text_id = qt.query_text_id
            JOIN sys.query_store_plan p ON q.query_id = p.query_id
            JOIN sys.query_store_runtime_stats rs ON p.plan_id = rs.plan_id
            WHERE rs.last_execution_time > DATEADD(HOUR, -24, SYSUTCDATETIME())
            ORDER BY rs.avg_duration DESC
            """);
    }

    [Fact]
    public void Registration_FromTheSkill_Builds()
    {
        int[] azureTransientErrors = [4060, 40197, 40501, 40613, 49918, 49919, 49920];

        // SQL Server in our own data centre
        var onPremises = new DbContextOptionsBuilder<TemporalDbContext>()
            .UseSqlServer(db.ConnectionString, sql =>
            {
                sql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null);
                sql.CommandTimeout(30);
            })
            .Options;

        // Azure SQL: UseAzureSql replaces UseSqlServer (UseAzureSqlDefaults is obsolete in EF 10)
        var azure = new DbContextOptionsBuilder<TemporalDbContext>()
            .UseAzureSql(db.ConnectionString, sql =>
            {
                sql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: azureTransientErrors);
                sql.CommandTimeout(30);
            })
            .Options;

        foreach (var options in new DbContextOptions<TemporalDbContext>[] { onPremises, azure })
        {
            using var context = new TemporalDbContext(options);
            Assert.True(context.Database.IsSqlServer());
        }
    }
}

// No database fixture: under these conditions SqlClient can't open a connection to create one.
public sealed class SqlServerSlimImageTests
{
    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Production)]
    public async Task Production_NoIcu_TheServiceStarts_AndItsFirstQueryFails()
    {
        ProductionConditions.Require();
        var server = Environment.GetEnvironmentVariable("MSSQL_URL")
            ?? "Server=localhost,1433;User Id=sa;Password=Samples-2026!;Encrypt=True;TrustServerCertificate=True";

        // Building the connection is fine, so nothing fails at start-up unless a check opens one...
        await using var connection = new SqlConnection(server);

        // ...and the first request's query is where it breaks: Dapper opens the connection and SqlClient refuses.
        var refused = await Assert.ThrowsAsync<NotSupportedException>(() =>
            connection.QueryAsync<int>("SELECT 1"));

        Assert.Contains("Globalization Invariant Mode is not supported", refused.Message);
    }
}
