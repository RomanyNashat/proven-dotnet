---
name: sqlserver-patterns
description: SQL Server for .NET — column rules, registration (UseSqlServer / UseAzureSql), READ_COMMITTED_SNAPSHOT, safe upserts (UPDLOCK, HOLDLOCK), table-valued parameters, SqlBulkCopy, bounded JSON, temporal tables, row-level security with session context, indexes, Query Store. Claims tested in CI against SQL Server 2022.
version: 2.1.0
---

# SQL Server Patterns

What's specific to SQL Server here, and the places where the common example breaks these rules. The code
marked as a sample, and every claim marked *tested*, runs in CI against SQL Server 2022 Developer
edition (`tests/SkillSamples.Tests/MsSql`). Engine-neutral data access (EF Core, Dapper, the outbox) is
in the skills that test it on both engines; `postgresql-patterns` is the other engine.

## 1. Column rules (the same as PostgreSQL)

Full text in `rules/efcore-rules.md`; the SQL Server shape of each:

| Rule | SQL Server |
|------|------------|
| Strings bounded | `nvarchar(n)` / `varchar(n)` with `HasMaxLength(n)` + `IsUnicode(...)`. **No `(max)`**. Ceiling 4,000 |
| Arabic text | **`nvarchar(n)`**. A `varchar` column stores Arabic as `???` with no error (tested, `localization` §8) |
| No binary data | No `varbinary`/`image`. Files go to object storage; the row keeps a bounded key |
| Keys | `int` `IDENTITY` (`UseIdentityColumn()`); `bigint` only for tables that can pass ~2 billion rows. No GUID keys, no `NEWSEQUENTIALID()` |
| Timestamps | `datetime2(3)` holding UTC; a `DateTimeOffset` property needs the converter (`efcore-patterns` §2, tested) |
| Money | `decimal(p,s)`. Never `money` |
| JSON | `nvarchar(n)` with an `ISJSON` check (§7); no `nvarchar(max)` |
| Concurrency | `rowversion` (`efcore-patterns` §8, tested) |

## 2. Registration

```csharp
// SQL Server in our own data centre
builder.Services.AddDbContextPool<AppDbContext>(options =>
    options.UseSqlServer(connectionString, sql =>
    {
        sql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null);
        sql.CommandTimeout(30);
    }), poolSize: 128);

// Azure SQL: UseAzureSql in place of UseSqlServer
builder.Services.AddDbContextPool<AppDbContext>(options =>
    options.UseAzureSql(connectionString, sql =>
        sql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(10),
            errorNumbersToAdd: [4060, 40197, 40501, 40613, 49918, 49919, 49920])), poolSize: 128);
```
Both compile and build a SQL Server context in CI. **`UseAzureSqlDefaults()` is obsolete in EF 10**
(the compiler says so), and with warnings as errors it breaks the build: use `UseAzureSql`.
- A transaction your code opens needs the execution strategy when retries are on (`efcore-patterns` §7,
  tested).
- **The image needs ICU.** `Microsoft.Data.SqlClient` refuses to connect in globalization-invariant
  mode ("Globalization Invariant Mode is not supported"), which is how slim and Alpine images run. Tested:
  the service starts, because building a connection opens nothing, and its first query fails. Install ICU
  in the image and turn invariant mode off; a start-up check that opens one connection makes it fail at
  deploy instead of on the first request. (Npgsql doesn't need ICU.)
- Dapper: a new `SqlConnection` per operation from `Microsoft.Data.SqlClient`, pooled by the driver.
  Never register a `SqlConnection` as a scoped or singleton service (`dapper-patterns` §3).

Connection string:
```
Server=...;Database=...;Encrypt=True;Application Name=OrderService;Connect Timeout=15;Max Pool Size=100;
```
- `Encrypt=True` and a real certificate. `TrustServerCertificate=True` only for a local container.
- `Application Name` shows in `sys.dm_exec_sessions`, so the DBA can see which service holds connections.
- `pods × Max Pool Size` (default 100) is a number to agree with the DBA. Leave `Min Pool Size` at 0
  unless a measured cold start needs it: every pod would hold that many connections open at all times.
- Leave MARS off (`MultipleActiveResultSets=False`, the default).
- Azure SQL with managed identity: `Authentication=Active Directory Default`, no password
  (`azure-deployment`).

## 3. Readers and writers: READ_COMMITTED_SNAPSHOT

SQL Server's default `READ COMMITTED` takes shared locks, so **a reader waits for any writer holding the
row**. Tested: while one transaction has updated a row, a plain `SELECT` of that row times out. After
`ALTER DATABASE … SET READ_COMMITTED_SNAPSHOT ON`, the same `SELECT` returns the last committed value at
once, without a dirty read (tested).
- Azure SQL has it on by default; an on-premises database has it off. Ask the DBA which yours runs; it
  changes how every query in the service behaves under load.
- `WITH (NOLOCK)` is not the fix: it reads uncommitted rows, and rows twice or not at all during page
  splits. It needs a documented reason (`dba-reviewer`).
- Writers still block writers. RCSI uses tempdb for row versions; the DBA sizes it.

## 4. Upserts and many ids

<!-- sample: tests/SkillSamples.Tests/MsSql/Inventory.cs -->
```csharp
public static class Inventory
{
    // UPDLOCK + HOLDLOCK hold the key range from the UPDATE to the INSERT, so a second caller waits and
    // then updates. Without them both see no row and both insert: one fails on the primary key.
    public static Task SaveAsync(SqlConnection connection, int productId, int warehouseId, int quantity, CancellationToken ct) =>
        connection.ExecuteAsync(new CommandDefinition("""
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;
            UPDATE product_inventory WITH (UPDLOCK, HOLDLOCK)
               SET quantity = @quantity
             WHERE product_id = @productId AND warehouse_id = @warehouseId;
            IF @@ROWCOUNT = 0
                INSERT INTO product_inventory (product_id, warehouse_id, quantity) VALUES (@productId, @warehouseId, @quantity);
            COMMIT;
            """, new { productId, warehouseId, quantity }, cancellationToken: ct));

    // MERGE is one statement but not atomic on its own: it needs HOLDLOCK for the same reason.
    public static Task MergeAsync(SqlConnection connection, int productId, int warehouseId, int quantity, CancellationToken ct) =>
        connection.ExecuteAsync(new CommandDefinition("""
            MERGE INTO product_inventory WITH (HOLDLOCK) AS target
            USING (VALUES (@productId, @warehouseId, @quantity)) AS source (product_id, warehouse_id, quantity)
               ON target.product_id = source.product_id AND target.warehouse_id = source.warehouse_id
            WHEN MATCHED THEN UPDATE SET quantity = source.quantity
            WHEN NOT MATCHED THEN INSERT (product_id, warehouse_id, quantity) VALUES (source.product_id, source.warehouse_id, source.quantity);
            """, new { productId, warehouseId, quantity }, cancellationToken: ct));

    // Any number of ids in one parameter, typed and indexed: a table-valued parameter.
    // Needs once, in a reviewed script: CREATE TYPE dbo.int_list AS TABLE (id int NOT NULL PRIMARY KEY);
    public static async Task<IReadOnlyList<int>> InStockAsync(SqlConnection connection, IEnumerable<int> productIds, CancellationToken ct)
    {
        using var ids = new DataTable();
        ids.Columns.Add("id", typeof(int));
        foreach (var id in productIds.Distinct())
        {
            ids.Rows.Add(id);
        }

        var rows = await connection.QueryAsync<int>(new CommandDefinition("""
            SELECT DISTINCT i.product_id
              FROM product_inventory i
              JOIN @ids p ON p.id = i.product_id
             WHERE i.quantity > 0
            """, new { ids = ids.AsTableValuedParameter("dbo.int_list") }, cancellationToken: ct));
        return [.. rows];
    }
}
```

Tested: ten saves of the same new key at the same moment leave one row and raise no error, with either
statement. A plain `IF NOT EXISTS … INSERT`, or `MERGE` without `HOLDLOCK`, lets two callers both see "no
row" and both insert. As a story: two scanners count a new product at once. Without hints both update
nothing, the second insert fails with 2627 and its count is lost; with `SaveAsync` the second waits on the
first's key-range lock, then updates. The TVP filters by the list in one typed parameter; `dapper-patterns` §2 has the
`OPENJSON` alternative, which needs no type. Mind the 2,100-parameter limit of `IN @ids`.

## 5. Bulk inserts

<!-- sample: tests/SkillSamples.Tests/MsSql/AuditBulkCopy.cs -->
```csharp
public static class AuditBulkCopy
{
    /// <summary>SqlBulkCopy streams the rows in batches; the identity column fills itself.</summary>
    public static async Task InsertAsync(string connectionString, IEnumerable<AuditEvent> events, CancellationToken ct)
    {
        using var table = new DataTable();
        table.Columns.Add("user_id", typeof(int));
        table.Columns.Add("action", typeof(string));
        table.Columns.Add("created_at", typeof(DateTime));
        foreach (var e in events)
        {
            table.Rows.Add(e.UserId, e.Action, e.CreatedAt.UtcDateTime);   // datetime2(3) holds UTC
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        using var bulkCopy = new SqlBulkCopy(connection) { DestinationTableName = "audit_events", BatchSize = 5000 };
        foreach (DataColumn column in table.Columns)
        {
            bulkCopy.ColumnMappings.Add(column.ColumnName, column.ColumnName);   // by name, not position
        }

        await bulkCopy.WriteToServerAsync(table, ct);
    }
}
```

Tested: 10,000 rows, each with its own identity value. Map columns by name; without mappings
`SqlBulkCopy` goes by position, and a new column in the middle of the table shifts every value.

## 6. Many rows in, many rows out

`OUTPUT inserted.id` returns new ids (EF does this for you); `UPDATE TOP (@n) … OUTPUT` with
`UPDLOCK, READPAST, ROWLOCK` claims a batch across pods (`worker-patterns`, tested on both engines).

## 7. JSON, bounded

```sql
CREATE TABLE tagged_orders (
    id int IDENTITY PRIMARY KEY,
    tags nvarchar(400) NOT NULL CONSTRAINT ck_tagged_orders_tags CHECK (ISJSON(tags) = 1),
    metadata nvarchar(1000) NOT NULL CONSTRAINT ck_tagged_orders_metadata CHECK (ISJSON(metadata) = 1),
    region AS CAST(JSON_VALUE(metadata, '$.region') AS varchar(10)) PERSISTED);   -- the field you filter on
CREATE INDEX ix_tagged_orders_region ON tagged_orders (region);
```
```sql
SELECT COUNT(*) FROM tagged_orders o CROSS APPLY OPENJSON(o.tags) t WHERE t.value = @tag;
SELECT COUNT(*) FROM tagged_orders WHERE region = @region;      -- uses the index
```
Tested: invalid JSON is rejected (CHECK, error 547); `OPENJSON` finds a tag; the persisted computed
column filters by a JSON field. `WHERE JSON_VALUE(metadata, '$.region') = @r` works too, but scans: a
field you filter on gets a computed column and an index, or a real column. The native `json` type
(SQL Server 2025, Azure SQL) is `(max)`-sized; it needs the DBA exception like any unbounded column.

## 8. Temporal tables (history kept by SQL Server)

<!-- sample: tests/SkillSamples.Tests/MsSql/TemporalOrders.cs -->
```csharp
public sealed class TemporalOrderConfiguration : IEntityTypeConfiguration<TemporalOrder>
{
    public void Configure(EntityTypeBuilder<TemporalOrder> builder)
    {
        // SQL Server keeps every previous version of a row in the history table, with the period it was valid.
        builder.ToTable("orders", t => t.IsTemporal(history =>
        {
            history.HasPeriodStart("valid_from");
            history.HasPeriodEnd("valid_to");
            history.UseHistoryTable("orders_history");
        }));
        builder.Property(o => o.Id).HasColumnName("id").UseIdentityColumn();
        builder.Property(o => o.Status).HasColumnName("status").HasMaxLength(20).IsUnicode(false);
    }
}
```

```csharp
var asItWas = await db.Orders.TemporalAsOf(pointInTimeUtc).SingleAsync(o => o.Id == id, ct);
var versions = await db.Orders.TemporalAll().Where(o => o.Id == id).ToListAsync(ct);
```
Tested: after a status change, `TemporalAsOf` a moment before returns the old status, and `TemporalAll`
returns both versions. The period columns are UTC. The history table grows with every update: agree a
retention with the DBA (`HISTORY_RETENTION_PERIOD`).

## 9. Row-level security

The database filters rows by tenant, under any query, including Dapper and ad-hoc SQL:
```sql
CREATE FUNCTION dbo.fn_tenant_filter(@tenant_id int)
RETURNS TABLE WITH SCHEMABINDING
AS RETURN SELECT 1 AS allowed WHERE @tenant_id = CAST(SESSION_CONTEXT(N'tenant_id') AS int);

CREATE SECURITY POLICY dbo.tenant_policy
    ADD FILTER PREDICATE dbo.fn_tenant_filter(tenant_id) ON dbo.records,
    ADD BLOCK PREDICATE dbo.fn_tenant_filter(tenant_id) ON dbo.records AFTER INSERT
WITH (STATE = ON);
```

<!-- sample: tests/SkillSamples.Tests/MsSql/TenantSession.cs -->
```csharp
/// <summary>
/// Tells SQL Server who the tenant is on every connection open; the security policy filters by it. The
/// tenant comes from the context being opened, not from a service injected here: interceptors in a
/// pooled context's options are built once, so an injected scoped service would be the first request's.
/// </summary>
public sealed class TenantSessionInterceptor : DbConnectionInterceptor
{
    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not TenantDbContext context)
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "EXEC sp_set_session_context @key = N'tenant_id', @value = @tenant, @read_only = 1;";
        command.Parameters.Add(new SqlParameter("@tenant", context.TenantId));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
```

Tested: each tenant sees only its rows; inserting a row for another tenant fails (block predicate, error
33504); `@read_only = 1` stops the value being changed later on that connection (error 15664); and a
pooled connection that's reused starts with the session context cleared, so a tenant can't leak to the
next request.
- **The tenant comes from the context, not an injected service.** The common example injects
  `ITenantService` into the interceptor; registered in a pooled context's options, that's the first
  request's tenant for every request (the same trap as `efcore-patterns` §4).
- RLS is a second wall, not the only one: queries still filter by owner (`rules/security.md`).

## 10. Indexes

```sql
CREATE NONCLUSTERED INDEX ix_orders_customer_status ON orders (customer_id, status) INCLUDE (total, created_at);   -- covering
CREATE NONCLUSTERED INDEX ix_orders_pending ON orders (created_at DESC) WHERE status = 'Pending' AND is_deleted = 0; -- filtered
CREATE NONCLUSTERED COLUMNSTORE INDEX ix_orders_analytics ON orders (customer_id, status, total, created_at);      -- reporting
ALTER INDEX ix_orders_customer_status ON orders REBUILD WITH (ONLINE = ON, MAXDOP = 4);
```
All four build in CI (Developer edition). **`ONLINE = ON` is an Enterprise feature**: Developer has it,
Standard doesn't. Check the production edition before a script relies on it; without it the rebuild
locks the table, so it goes in a maintenance window.
- A filtered index serves a query only when the optimizer can see the filter matches. A parameter
  (`WHERE status = @status`) can't prove that, so write the literal the filter uses.
- A columnstore index on a busy OLTP table costs every write. Agree it with the DBA.

## 11. Query Store

```sql
ALTER DATABASE [appdb] SET QUERY_STORE = ON (
    OPERATION_MODE = READ_WRITE, DATA_FLUSH_INTERVAL_SECONDS = 900, MAX_STORAGE_SIZE_MB = 1024,
    INTERVAL_LENGTH_MINUTES = 60, QUERY_CAPTURE_MODE = AUTO);

SELECT TOP 10 q.query_id, qt.query_sql_text, rs.avg_duration / 1000.0 AS avg_duration_ms, rs.count_executions
FROM sys.query_store_query q
JOIN sys.query_store_query_text qt ON q.query_text_id = qt.query_text_id
JOIN sys.query_store_plan p ON q.query_id = p.query_id
JOIN sys.query_store_runtime_stats rs ON p.plan_id = rs.plan_id
WHERE rs.last_execution_time > DATEADD(HOUR, -24, SYSUTCDATETIME())
ORDER BY rs.avg_duration DESC;
```
Both run in CI. `sp_query_store_force_plan` pins a plan for a regressed query; it's a production
change, so it goes through the DBA. Read plans with `SET STATISTICS IO, TIME ON` and the actual plan.

## 12. Always Encrypted

Not run in CI: it needs a column master key in a key store (Azure Key Vault or a Windows certificate
store). The rules still apply:
- `DETERMINISTIC` allows equality (`WHERE national_id = @id`) but shows which rows hold the same value;
  `RANDOMIZED` allows nothing but storage and retrieval.
- Encrypted columns stay bounded: `nvarchar(20)` for a national ID, never `nvarchar(max)` for notes. Long
  free text goes to object storage, encrypted there (`encryption-patterns`).
- The connection needs `Column Encryption Setting=Enabled`, and parameters must be typed (Dapper with
  `DbString` / explicit sizes) or the driver can't encrypt them.
- For field-level encryption the app controls instead, see `encryption-patterns` and `pii-masking`.

## 13. Review checklist (with `dba-reviewer`)
- A GUID key or `NEWSEQUENTIALID()`; `(max)` anywhere, including encrypted and JSON columns.
- Arabic text in `varchar`; a `DateTimeOffset` on `datetime2` without the UTC converter.
- `UseAzureSqlDefaults()` (obsolete); a scoped or singleton `SqlConnection`.
- `IF NOT EXISTS … INSERT` or `MERGE` without `HOLDLOCK`.
- `NOLOCK` without a written reason; assuming RCSI is on (or off) without checking.
- An RLS interceptor that takes the tenant from an injected service.
- `ONLINE = ON` in a script for a Standard-edition server.
