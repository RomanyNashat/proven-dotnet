---
name: postgresql-patterns
description: PostgreSQL for .NET with Npgsql/EF Core: column rules, SQL Server→PG gotchas, pooling, safe migrations (what rewrites a table, CONCURRENTLY), JSONB, full-text, upserts, COPY, partitions, advisory locks. Claims tested in CI against PostgreSQL 17.
version: 2.2.0
---

# PostgreSQL Patterns

Most engineers who move to PostgreSQL arrive from SQL Server, so this skill leads with the
column rules and with what behaves differently, then the patterns. The code marked as a sample, and every
claim marked *tested*, runs in CI against PostgreSQL 17 (`tests/SkillSamples.Tests/Postgres`). Engine-neutral
data access (EF Core, Dapper, the outbox) is in the skills that test it on both engines.

## 1. Column rules (the same as SQL Server)

Full text in `rules/efcore-rules.md`; the PostgreSQL shape of each:

| Rule | PostgreSQL |
|------|------------|
| Strings bounded | `varchar(n)` with `HasMaxLength(n)`. **No `text`**, no unbounded `varchar`. Ceiling 4,000 |
| No binary data | **No `bytea`.** Files/blobs → object storage, the row keeps a bounded key |
| Keys | `int` **`GENERATED ALWAYS AS IDENTITY`** (never `serial`). `bigint` only for tables that can pass ~2 billion rows |
| GUIDs | Minimal — only IDs created outside the DB. Native `uuid`, never `varchar(36)` |
| Timestamps | **`timestamptz`**, UTC. Never `timestamp` (without time zone) |
| Money | `numeric(p,s)`. Never the `money` type |
| JSON | **`jsonb` is allowed**. Fields you filter or join on still get real columns |
| Enums | `int`, or `varchar(n)` via `HasConversion<string>().HasMaxLength(n)`. Avoid PG enum types — adding a value needs `ALTER TYPE` |

<!-- sample: tests/SkillSamples.Tests/Postgres/PgOrders.cs -->
```csharp
public sealed class OrderConfiguration : IEntityTypeConfiguration<PgOrder>
{
    public void Configure(EntityTypeBuilder<PgOrder> builder)
    {
        builder.ToTable("orders");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).HasColumnName("id").UseIdentityAlwaysColumn();               // int identity, never serial
        builder.Property(o => o.Reference).HasColumnName("reference").HasMaxLength(30);          // varchar(30)
        builder.Property(o => o.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20);
        builder.Property(o => o.Total).HasColumnName("total").HasPrecision(18, 2);               // numeric(18,2)
        builder.Property(o => o.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz");
        builder.Property(o => o.Tags).HasColumnName("tags").HasColumnType("varchar(30)[]");    // string[] alone is text[]
    }
}
```

Tested: the columns come out as `character varying(30)`, `character varying(20)`, `numeric(18,2)`, and the
key as an `ALWAYS` identity; the enum is stored as its name.

## 2. Coming from SQL Server — what bites

| SQL Server habit | On PostgreSQL | Do this |
|------------------|---------------|---------|
| Comparisons are case-insensitive | **Case-sensitive by default.** `WHERE email = @e` misses `Ali@x.com` | Store a normalized (lower-case) column with a unique index, or use an ICU non-deterministic collation. `EF.Functions.ILike` for search (`=` misses, `ILIKE` finds: tested). (`citext` has no length — it breaks the bounded-string rule) |
| Any `DateTime` goes in | Through EF (or a parameter typed `timestamptz`) Npgsql **throws** for a `DateTime` whose `Kind` isn't `Utc`, and for a `DateTimeOffset` whose offset isn't 0. **Through a plain Dapper parameter it doesn't throw:** the value goes as `timestamp` and the server reads it in the session's time zone, so noon `Unspecified` under `Asia/Riyadh` is stored as 09:00 UTC (all tested) | Take time from `TimeProvider.GetUtcNow()` (already our rule). `DateTime.Parse` gives `Unspecified`: convert before saving. On a pod in UTC, `DateTime.Now` has the right clock reading but `Kind=Local`, and is refused all the same (tested on a slim image) |
| `PascalCase` names | Unquoted names fold to lower case; mixed case needs quotes everywhere | `snake_case`: `UseSnakeCaseNamingConvention()` (EFCore.NamingConventions) or explicit `ToTable`/`HasColumnName` |
| `WITH (NOLOCK)` | Not needed — MVCC: readers never block writers | Remove it; don't look for an equivalent |
| An error inside a transaction, carry on | **One error aborts the whole transaction** ("current transaction is aborted", `25P02`, tested) | Roll back and retry the unit; use `SAVEPOINT` only when you truly need partial recovery |
| `TOP`, `ISNULL`, `GETDATE()`, `+` for strings | `LIMIT`, `COALESCE`, `now()`, `\|\|` | — |
| `OUTPUT INSERTED.Id` / `SCOPE_IDENTITY()` | `RETURNING id` | EF does this for you |
| `WHERE id IN @ids` (Dapper expands) | Works, but one plan per list length | `WHERE id = ANY(@ids)` with an `int[]`: one plan (tested) |
| `NOT IN (subquery)` | One NULL in the subquery and it returns nothing (tested) | `NOT EXISTS` |
| `BETWEEN` on timestamps | Includes the end instant: a row at midnight counts in both days (tested) | `>= start AND < end` |

## 3. Connections and pooling

```csharp
// One NpgsqlDataSource per database — singleton, owns the pool.
builder.Services.AddNpgsqlDataSource(builder.Configuration.GetConnectionString("Default")!);

builder.Services.AddDbContextPool<AppDbContext>((sp, options) =>
    options.UseNpgsql(sp.GetRequiredService<NpgsqlDataSource>(), npgsql =>
    {
        npgsql.MigrationsAssembly("Infrastructure");
        npgsql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorCodesToAdd: null);
    })
    .UseSnakeCaseNamingConvention(), poolSize: 128);
```

Connection string (production):
```
Host=...;Database=...;Username=...;Password=...;
Maximum Pool Size=20;Minimum Pool Size=0;Timeout=15;Command Timeout=30;
```
- **Size the pool for the whole fleet, not one pod.** PostgreSQL allows **100 connections by default**.
  `pods × Maximum Pool Size` must stay under `max_connections` minus admin headroom. 10 pods × Npgsql's
  default of 100 = 1,000 — the database refuses connections long before that. Agree the number with the
  DBA.
- **Behind PgBouncer in transaction mode:** add `No Reset On Close=true` (or turn Npgsql pooling off with
  `Pooling=false`). Session-level features (`SET`, advisory session locks, `LISTEN`) don't survive a
  transaction-mode pooler.
- **Never `Include Error Detail=true` outside local development.** It puts parameter values into error
  messages, and those reach logs — on this platform that can be patient data.

### Retries need the execution strategy
`EnableRetryOnFailure` **throws** as soon as code opens its own transaction, unless the unit is wrapped
(tested on both engines in `efcore-patterns` §7, which has the helper):
```csharp
var strategy = db.Database.CreateExecutionStrategy();
await strategy.ExecuteAsync(async () =>
{
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    // ... changes ...
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
});
```
The whole lambda re-runs on a transient failure, so it must be safe to repeat.

## 4. Keys: identity, and HiLo when you need the ID early

```csharp
// Default: database assigns the int at INSERT
modelBuilder.UseIdentityAlwaysColumns();

// When the domain needs the ID before SaveChanges (e.g. inside a domain event):
modelBuilder.UseHiLo("order_ids");     // int/bigint from a sequence, fetched in blocks — still no GUID
```

## 5. Safe schema changes

Migrations are reviewed scripts (`rules/efcore-rules.md`). PostgreSQL-specific rules for those scripts:

- **Create indexes on existing tables concurrently**: a plain `CREATE INDEX` blocks writes for the whole
  build.
  ```csharp
  builder.HasIndex(o => o.CustomerId).IsCreatedConcurrently();
  ```
  **Known bug:** `dotnet ef migrations script --idempotent` wraps it in a `DO` block, which PostgreSQL
  rejects ("cannot be executed from a function"): npgsql/efcore.pg #3921, open at 10.0.3. **A migration
  with a concurrent index is scripted on its own, without `--idempotent`**, and run outside a
  transaction. `/migrate` does this. Tested: inside a `DO` block and inside a transaction it fails; on
  its own it works.
- **Set a lock timeout at the top of every script:** `SET lock_timeout = '5s';` A DDL statement waiting
  behind a long transaction otherwise queues every query behind it.
- **Foreign keys and CHECKs on big tables:** add `NOT VALID`, then `VALIDATE CONSTRAINT` separately —
  validation doesn't block writes.
  ```sql
  ALTER TABLE orders ADD CONSTRAINT fk_orders_customer FOREIGN KEY (customer_id) REFERENCES customers (id) NOT VALID;
  ALTER TABLE orders VALIDATE CONSTRAINT fk_orders_customer;
  ```
- **Adding a column with a default is instant unless the default is volatile** (PostgreSQL 11+). A
  constant and `now()` are both instant: `now()` is fixed for the statement, so every existing row gets
  the same value. `clock_timestamp()`, `random()` and `gen_random_uuid()` give each row its own value
  and rewrite the table: add the column nullable, backfill in batches, then set the default. Tested
  (constant, `now()`, `clock_timestamp()`, `random()`) by whether the table's file on disk changes.
- **Widening `varchar(n)` is instant; narrowing rewrites the table, and fails if a value is longer**
  (both tested). Raising a limit is the cheap direction.

## 6. JSONB (allowed)

```csharp
// EF 10 + Npgsql 10: complex type stored as jsonb, with partial updates via jsonb_set
builder.ComplexProperty(p => p.Specs, s => s.ToJson());
```
```sql
CREATE INDEX CONCURRENTLY ix_products_specs ON products USING GIN (specs);            -- @>, ?, ?|
CREATE INDEX CONCURRENTLY ix_products_weight ON products (((specs->>'weight')::numeric)); -- one path
```
- Fields you filter, join or sort on belong in real columns — `jsonb` is for the genuinely variable part.
- **Npgsql 10 change:** `arrayColumn.Contains(x)` now translates to `x = ANY(arrayColumn)`, which a GIN
  index can't serve. If the column has a GIN index in the model (`HasIndex(...).HasMethod("gin")`),
  Npgsql keeps the containment translation (`@>`) that uses it, so declare the index in EF, not only
  in SQL. Tested: the same query gives `= ANY (` without the index in the model and `@>` with it.
- A `string[]` column needs its element type (`HasColumnType("varchar(30)[]")`, as in §1); alone it's
  `text[]`, which breaks the bounded-string rule.

## 7. Full-text search

<!-- sample: tests/SkillSamples.Tests/Postgres/PgProducts.cs -->
```csharp
public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products");
        builder.Property(p => p.Id).HasColumnName("id").UseIdentityAlwaysColumn();
        builder.Property(p => p.Name).HasColumnName("name").HasMaxLength(200);
        builder.Property(p => p.Description).HasColumnName("description").HasMaxLength(1000);

        // 'simple' splits words without stemming: right for Arabic and mixed text ('english' stems English only).
        builder.Property(p => p.SearchVector)
            .HasColumnName("search_vector")
            .HasColumnType("tsvector")
            .HasComputedColumnSql("to_tsvector('simple', coalesce(name, '') || ' ' || coalesce(description, ''))", stored: true);
        builder.HasIndex(p => p.SearchVector).HasMethod("GIN");
    }
}
```

```csharp
var names = await db.Products
    .Where(p => p.SearchVector.Matches(EF.Functions.PlainToTsQuery("simple", term)))
    .OrderByDescending(p => p.SearchVector.Rank(EF.Functions.PlainToTsQuery("simple", term)))
    .Select(p => p.Name)
    .Take(20)
    .ToListAsync(ct);
```
Tested: an Arabic word in the description finds the product, and an English word finds the other one.
For Arabic spelling variants (أحمد/احمد), normalize first (`localization` §9).

## 8. Upserts (ON CONFLICT)

<!-- sample: tests/SkillSamples.Tests/Postgres/Preferences.cs -->
```csharp
public static class Preferences
{
    // One statement: two requests at once can't both insert; the second updates.
    public static Task SaveAsync(NpgsqlConnection connection, int userId, string key, string value, DateTimeOffset now, CancellationToken ct) =>
        connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO user_preferences (user_id, preference_key, preference_value, updated_at)
            VALUES (@userId, @key, @value, @now)
            ON CONFLICT (user_id, preference_key)
            DO UPDATE SET preference_value = EXCLUDED.preference_value, updated_at = EXCLUDED.updated_at
            """, new { userId, key, value, now }, cancellationToken: ct));

    // Many rows in one statement: one array per column, unnest pairs them up by position.
    public static Task SaveStockAsync(NpgsqlConnection connection, int[] productIds, int[] warehouseIds, int[] quantities, CancellationToken ct) =>
        connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO product_inventory (product_id, warehouse_id, quantity)
            SELECT * FROM unnest(@productIds, @warehouseIds, @quantities)
            ON CONFLICT (product_id, warehouse_id) DO UPDATE SET quantity = EXCLUDED.quantity
            """, new { productIds, warehouseIds, quantities }, cancellationToken: ct));
}
```

Tested: ten saves of one key at the same moment leave one row; the batch inserts new pairs and updates
existing ones. Never SELECT-then-INSERT: two requests race between the two statements. Dapper passes a
C# array as a PostgreSQL array (no list expansion), which is what `unnest` needs.

## 9. Bulk load (COPY)

<!-- sample: tests/SkillSamples.Tests/Postgres/AuditImport.cs -->
```csharp
public static class AuditImport
{
    /// <summary>COPY in binary: one round trip for the whole set. The identity column fills itself.</summary>
    public static async Task<ulong> ImportAsync(NpgsqlDataSource dataSource, IEnumerable<AuditEvent> events, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var writer = await connection.BeginBinaryImportAsync(
            "COPY audit_events (user_id, action, entity_type, entity_id, created_at) FROM STDIN (FORMAT BINARY)", ct);

        foreach (var e in events)
        {
            await writer.StartRowAsync(ct);
            await writer.WriteAsync(e.UserId, NpgsqlDbType.Integer, ct);
            await writer.WriteAsync(e.Action, NpgsqlDbType.Varchar, ct);
            await writer.WriteAsync(e.EntityType, NpgsqlDbType.Varchar, ct);
            await writer.WriteAsync(e.EntityId, NpgsqlDbType.Integer, ct);
            await writer.WriteAsync(e.CreatedAt, NpgsqlDbType.TimestampTz, ct);   // offset 0
        }

        return await writer.CompleteAsync(ct);   // nothing is saved without this
    }
}
```

Tested: 10,000 rows in one call, each with its own identity value. Tens of thousands of rows per second,
against hundreds with batched INSERTs. Every value needs `Kind=Utc` / offset 0, as in §2.

## 10. Partitioning (high-volume, time-based tables)

```sql
CREATE TABLE audit_events (
    id          bigint GENERATED ALWAYS AS IDENTITY,
    user_id     int          NOT NULL,
    action      varchar(50)  NOT NULL,
    created_at  timestamptz  NOT NULL DEFAULT now(),
    PRIMARY KEY (id, created_at)                 -- the partition key must be part of the PK
) PARTITION BY RANGE (created_at);

CREATE TABLE audit_events_2026_10 PARTITION OF audit_events
    FOR VALUES FROM ('2026-10-01') TO ('2026-11-01');
```
Create partitions ahead of time (a scheduled job or pg_partman): an INSERT with no matching partition
fails ("no partition of relation found for row"), and a primary key without the partition key is refused
at `CREATE TABLE` (both tested). Retention becomes `DROP TABLE` on an old partition instead of a huge `DELETE`.

## 11. Advisory locks (one worker at a time)

A session lock (`pg_advisory_lock`) belongs to the **physical connection** that took it. Disposing the
connection returns it to the pool still connected, so the lock stays held, and no other connection can
release it (`pg_advisory_unlock` there returns false); both tested. Use the **transaction-scoped** lock,
which frees itself at commit or rollback:

<!-- sample: tests/SkillSamples.Tests/Postgres/ExclusiveRunner.cs -->
```csharp
public sealed class ExclusiveRunner(NpgsqlDataSource dataSource)
{
    /// <summary>
    /// Runs the work only if no one else holds the lock. The lock belongs to the transaction: commit,
    /// rollback, or a dropped connection releases it, so a crashed pod can't keep it.
    /// </summary>
    public async Task<bool> RunExclusiveAsync(long lockKey, Func<NpgsqlConnection, NpgsqlTransaction, Task> work, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var acquired = await connection.ExecuteScalarAsync<bool>(
            new CommandDefinition("SELECT pg_try_advisory_xact_lock(@lockKey)", new { lockKey }, tx, cancellationToken: ct));
        if (!acquired)
        {
            return false;   // someone else holds it
        }

        await work(connection, tx);
        await tx.CommitAsync(ct);   // the lock goes with the commit
        return true;
    }
}
```

Tested: while one caller is inside, a second is turned away; after the commit, the next one gets in.
As a story: the pod running the nightly job dies halfway; its lock and its half-done rows go with the
transaction, the next pod runs the job once, and a third that starts meanwhile is turned away.
Session locks don't work behind a transaction-mode PgBouncer either. The EF version of this lock is the
`outbox` relay's, tested beside SQL Server's `sp_getapplock`.

## 12. Diagnosing

```sql
-- Top queries by total time (needs the pg_stat_statements extension — ask the DBA)
SELECT query, calls, round(total_exec_time) AS total_ms, round(mean_exec_time, 1) AS mean_ms
FROM pg_stat_statements ORDER BY total_exec_time DESC LIMIT 20;

-- Running longer than 5 seconds
SELECT pid, now() - query_start AS duration, state, query
FROM pg_stat_activity
WHERE state <> 'idle' AND now() - query_start > interval '5 seconds'
ORDER BY duration DESC;

-- Dead tuples (autovacuum falling behind)
SELECT relname, n_dead_tup, n_live_tup FROM pg_stat_user_tables WHERE n_dead_tup > 10000 ORDER BY n_dead_tup DESC;

-- Unused indexes (cost on every write, help nothing)
SELECT indexrelname, idx_scan, pg_size_pretty(pg_relation_size(indexrelid)) AS size
FROM pg_stat_user_indexes WHERE idx_scan = 0 ORDER BY pg_relation_size(indexrelid) DESC;
```
The last three run in CI. Read plans with `EXPLAIN (ANALYZE, BUFFERS)` on a copy, since ANALYZE runs the query.

## See also
- `outbox` — logical replication (CDC) or `FOR UPDATE SKIP LOCKED` polling for the relay.
- `efcore-patterns`, `dapper-patterns` — engine-neutral data access.
- `sqlserver-patterns` — the engine most services are coming from.
