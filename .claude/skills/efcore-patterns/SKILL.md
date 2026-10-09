---
name: efcore-patterns
description: EF Core 10/8, on PostgreSQL and SQL Server — pooled contexts with per-request state, query filters that don't leak between callers, audit interceptors, retries with transactions, concurrency, bulk updates, reviewed migration scripts, DBA column rules. Core code tested in CI on both engines.
version: 2.1.0
---

# EF Core Patterns

The model knows EF Core. This skill holds what's specific here: these rules, and the places where the
common example is wrong for us. The code marked as a sample is tested in CI against **both**
PostgreSQL 17 and SQL Server 2022 (`tests/SkillSamples.Tests/EfCore`): the same tests run on each engine.

The hard bans (no `Migrate()` in the app, no unbounded or binary columns, `int` keys) are in
`rules/efcore-rules.md` and always loaded. Related: `postgresql-patterns` and `sqlserver-patterns` (engine
specifics, concurrent indexes), `dapper-patterns` (reads and reports), `outbox` (events and messages),
`/migrate` (scripts).

## 1. Registration: a pooled context with per-request state

<!-- sample: tests/SkillSamples.Tests/EfCore/VisitsRegistration.cs -->
```csharp
public interface ICaller
{
    int Id { get; }
}

public static class VisitsRegistration
{
    /// <summary>
    /// A pooled context is reused across requests, so it can't take per-request services in its
    /// constructor. Rent it from the pooled factory and set the caller on every rental. Inject the
    /// context, never the factory: a rental that skips this keeps the previous request's caller.
    /// </summary>
    public static IServiceCollection AddVisitsDb(this IServiceCollection services, Action<DbContextOptionsBuilder> useDatabase)
    {
        // useDatabase: o => o.UseNpgsql(cs, pg => pg.EnableRetryOnFailure(3))
        //          or  o => o.UseSqlServer(cs, sql => sql.EnableRetryOnFailure(3))
        services.AddPooledDbContextFactory<VisitsDbContext>(
            (sp, options) =>
            {
                useDatabase(options);
                options.AddInterceptors(new AuditInterceptor(sp.GetRequiredService<TimeProvider>()));
            },
            poolSize: 128);

        services.AddScoped(sp =>
        {
            var db = sp.GetRequiredService<IDbContextFactory<VisitsDbContext>>().CreateDbContext();
            db.CallerId = sp.GetRequiredService<ICaller>().Id;
            return db;   // disposed with the scope, which returns it to the pool
        });

        return services;
    }
}
```

- A pooled context can't take scoped services (the current user, the tenant) in its constructor. Set
  them on each rental, as above. A context with no per-request state can use `AddDbContextPool` directly.
- Tested: a context rented from the factory directly keeps the previous request's caller. Inject the
  context, never the factory.
- Pool size against the database: `pods × Max Pool Size` (the driver's connection pool, Npgsql or
  SqlClient, not this one) must fit what the server allows: PostgreSQL's `max_connections` defaults
  to 100 (`rules/performance.md`).
- `UseSnakeCaseNamingConvention()` (EFCore.NamingConventions) only where the service already uses it;
  don't mix it with explicit names in one model.

## 2. Entity configuration and column types

<!-- sample: tests/SkillSamples.Tests/EfCore/VisitConfiguration.cs -->
```csharp
public enum Engine { PostgreSql, SqlServer }

/// <summary>
/// The same entity under the column rules on each engine. A real service has one engine and keeps one
/// branch; both are here so CI checks both.
/// </summary>
public sealed class VisitConfiguration(Engine engine) : IEntityTypeConfiguration<Visit>
{
    /// <summary>The concurrency token, a shadow property: its type differs by engine (see VersionToken).</summary>
    public const string Version = nameof(Version);

    public void Configure(EntityTypeBuilder<Visit> builder)
    {
        builder.ToTable("visits");
        builder.Property(v => v.Notes).HasMaxLength(500).IsUnicode().IsRequired();   // no length = text / nvarchar(max)
        builder.HasIndex(v => v.PatientId);

        if (engine == Engine.PostgreSql)
        {
            builder.Property(v => v.Id).UseIdentityAlwaysColumn();
            builder.Property(v => v.CreatedAt).HasColumnType("timestamptz");
            builder.Property(v => v.UpdatedAt).HasColumnType("timestamptz");
            builder.Property<uint>(Version).IsRowVersion();          // PostgreSQL's xmin: no column added
        }
        else
        {
            builder.Property(v => v.Id).UseIdentityColumn();
            // datetime2(3) in UTC. Without the converter EF maps DateTimeOffset to datetimeoffset.
            builder.Property(v => v.CreatedAt).HasColumnType("datetime2(3)")
                .HasConversion(v => v.UtcDateTime, v => new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)));
            builder.Property(v => v.UpdatedAt).HasColumnType("datetime2(3)")
                .HasConversion(v => v!.Value.UtcDateTime, v => (DateTimeOffset?)new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)));
            builder.Property<byte[]>(Version).IsRowVersion();        // a rowversion column
        }
    }
}
```

| Rule | SQL Server | PostgreSQL |
|------|------------|------------|
| Strings, 4,000 at most | `HasMaxLength(n)` + `IsUnicode(...)`: `nvarchar(n)` / `varchar(n)` | `HasMaxLength(n)`: `varchar(n)` |
| Keys | `UseIdentityColumn()` (`bigint` for huge tables) | `UseIdentityAlwaysColumn()`, never `serial` |
| Timestamps, UTC | `datetime2(3)` | `timestamptz` |
| Money | `decimal(p,s)` via `HasPrecision` | `numeric(p,s)` via `HasPrecision` |
| JSON | not allowed (it's `nvarchar(max)`) | `jsonb`, for the genuinely variable part |
| String arrays | — | `HasColumnType("varchar(50)[]")`; `string[]` alone is `text[]` |

Tested on each engine: the columns come out as the table says (`character varying(500)` /
`nvarchar(500)`, `timestamptz` / `datetime2(3)`, `xid` / `rowversion`).
- **A string with no `HasMaxLength` is an unbounded column.** The bug is made by leaving it out.
- **SQL Server and `DateTimeOffset`:** EF maps it to `datetimeoffset` unless told otherwise. Here,
  type is `datetime2(3)` in UTC, so the column needs the type *and* the converter above.
- One `IEntityTypeConfiguration<T>` per entity, applied with `ApplyConfigurationsFromAssembly`.
- Need the id before `SaveChanges` (for an event)? `UseHiLo()`, still an `int`, and add with `AddAsync`
  (HiLo may fetch the next block). GUIDs only for ids made outside the database, as `uuid` /
  `uniqueidentifier`.
- JSON on PostgreSQL: EF 10 maps complex types with `ComplexProperty(p => p.Details, d => d.ToJson())`;
  EF 8 and 9 use `OwnsOne(..., o => o.ToJson())`. Fields you filter or join on stay real columns.

## 3. Query filters that don't leak between callers

<!-- sample: tests/SkillSamples.Tests/EfCore/VisitsDbContext.cs -->
```csharp
public sealed class VisitsDbContext(DbContextOptions<VisitsDbContext> options) : DbContext(options)
{
    public const string OwnerFilter = nameof(OwnerFilter);
    public const string SoftDeleteFilter = nameof(SoftDeleteFilter);

    /// <summary>
    /// Set on every rental (see <see cref="VisitsRegistration"/>). The filter reads it from the context
    /// instance, so EF sends it as a parameter on each query. A value copied into a local inside
    /// OnModelCreating is baked into the model, which EF builds once per context type: every caller would
    /// then get the first caller's rows.
    /// </summary>
    public int CallerId { get; set; }

    public DbSet<Visit> Visits => Set<Visit>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new VisitConfiguration(Database.IsSqlServer() ? Engine.SqlServer : Engine.PostgreSql));

        // Filters that read the context live here; the rest of the mapping stays in the configuration class.
        modelBuilder.Entity<Visit>()
            .HasQueryFilter(OwnerFilter, v => v.PatientId == CallerId)
            .HasQueryFilter(SoftDeleteFilter, v => !v.IsDeleted);
    }
}
```

What the tests show:
- **Each caller sees only their rows**, through the pooled registration in §1.
- **The common version leaks.** Copying the caller into a local in `OnModelCreating`
  (`var tenantId = tenantService.GetCurrentTenantId();`, then `e => e.TenantId == tenantId`) bakes the
  first caller's value into the model, which EF builds once per context type. A test shows the second
  caller getting the first caller's rows. The filter must read a member of the context.
- `IgnoreQueryFilters([VisitsDbContext.OwnerFilter])` lets an admin query see every patient and still
  hides deleted rows. `IgnoreQueryFilters()` with no names drops all of them.

Named filters are EF 10. On EF 8, one anonymous filter per entity combines the conditions with `&&`,
and `IgnoreQueryFilters()` can only drop all of it. A filter is a convenience, not the ownership check:
`rules/security.md` still holds for every id lookup, including Dapper reads, which no filter covers.

## 4. Audit columns

<!-- sample: tests/SkillSamples.Tests/EfCore/AuditInterceptor.cs -->
```csharp
/// <summary>
/// Stateless, so one instance serves every pooled context. The user comes from the context being saved,
/// never from a scoped service captured when the pool was built (that would stamp one user on every save).
/// </summary>
public sealed class AuditInterceptor(TimeProvider time) : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is VisitsDbContext db)
        {
            var now = time.GetUtcNow();
            foreach (var entry in db.ChangeTracker.Entries<IAudited>())
            {
                if (entry.State == EntityState.Added)
                {
                    entry.Property(nameof(IAudited.CreatedAt)).CurrentValue = now;
                    entry.Property(nameof(IAudited.CreatedBy)).CurrentValue = db.CallerId;
                }
                else if (entry.State == EntityState.Modified)
                {
                    entry.Property(nameof(IAudited.UpdatedAt)).CurrentValue = now;
                    entry.Property(nameof(IAudited.UpdatedBy)).CurrentValue = db.CallerId;
                }
            }
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
```

- Tested: with one pooled context reused by two requests, each row records its own caller and the
  `TimeProvider` time.
- The common version injects `ICurrentUserService` into the interceptor and registers it in the pool's
  options. Those options are built once, so every save records the same user.
- Only the async path is overridden; the code here calls `SaveChangesAsync`. If anything calls the sync
  `SaveChanges`, override `SavingChanges` too.

## 5. Domain events and messages: the outbox

Events that leave the service (Kafka) go through the **outbox** (`outbox` skill, tested): the event row
is written in the same transaction as the change, and a relay publishes it.
- Don't publish from `SavingChangesAsync`: it runs **before** the save, so a failed save has already
  sent its events.
- Don't publish from `SavedChangesAsync` either: a crash between the commit and the publish loses the
  event.
- In-process handlers (no broker) can run after `SaveChangesAsync` returns, called by the command
  handler. No mediator library (`cqrs-eventsourcing`).

## 6. Bulk updates

```csharp
await db.Visits
    .Where(v => v.CreatedAt < cutoff)
    .ExecuteUpdateAsync(s => s
        .SetProperty(v => v.Notes, "")
        .SetProperty(v => v.UpdatedAt, time.GetUtcNow())   // interceptors don't run: set audit columns here
        .SetProperty(v => v.UpdatedBy, db.CallerId), ct);
```

Tested: `ExecuteUpdateAsync` keeps the query filters (a caller can't update another patient's rows) and
skips `SaveChanges` interceptors (the audit columns stay empty unless set). It also skips concurrency
tokens and the change tracker. EF 10 accepts a block lambda for conditional `SetProperty` calls.

## 7. Transactions with retries

<!-- sample: tests/SkillSamples.Tests/EfCore/TransactionalSave.cs -->
```csharp
public static class TransactionalSave
{
    /// <summary>
    /// With EnableRetryOnFailure, a transaction opened outside the execution strategy throws. Inside it, the
    /// whole unit runs again after a transient failure. So the unit creates its own entities and calls
    /// SaveChangesAsync itself, as often as it needs, and does only database work (no HTTP, no Kafka).
    /// Each attempt starts with an empty change tracker: entities from a rolled-back attempt would
    /// otherwise look saved and be skipped. Anything tracked before the call is dropped too.
    /// </summary>
    public static Task<T> InTransactionAsync<T>(this DbContext db, Func<CancellationToken, Task<T>> unit, CancellationToken ct) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(
            async attemptCt =>
            {
                db.ChangeTracker.Clear();
                await using var tx = await db.Database.BeginTransactionAsync(attemptCt);
                var result = await unit(attemptCt);
                await tx.CommitAsync(attemptCt);
                return result;
            },
            ct);
}
```

```csharp
var orderId = await db.InTransactionAsync(async ct =>
{
    var order = Order.Place(command.PatientId, command.Total);   // created inside: a retry starts again here
    db.Orders.Add(order);
    await db.SaveChangesAsync(ct);
    db.Payments.Add(Payment.Pending(order.Id, command.Total));
    await db.SaveChangesAsync(ct);
    return order.Id;
}, ct);
```

Tested: with `EnableRetryOnFailure`, a transaction opened directly throws `InvalidOperationException`;
the same work through the helper commits. With a transient failure on the second of two saves, the retry
commits both rows once.
- First ask whether one save does it: with a navigation (`order.AddPayment(...)`) EF inserts both in one
  `SaveChangesAsync`, which is already a transaction and is retried on its own.
- A commit whose reply is lost to a dropped connection is retried too, and can insert twice. Where that
  matters (payments), give the row a unique idempotency key.

## 8. Concurrency

The token is `xmin` (a `uint`) on PostgreSQL and a `rowversion` (8 bytes) on SQL Server, mapped as the
shadow property in §2. The client gets it as an opaque string and sends it back:

<!-- sample: tests/SkillSamples.Tests/EfCore/VersionToken.cs -->
```csharp
/// <summary>
/// The concurrency token as the client sees it: an opaque string. PostgreSQL's xmin is a uint, SQL
/// Server's rowversion is 8 bytes, and the client doesn't need to know which.
/// </summary>
public static class VersionToken
{
    public static string Read(DbContext db, object entity) =>
        db.Entry(entity).Property(VisitConfiguration.Version).CurrentValue switch
        {
            uint xmin => xmin.ToString(CultureInfo.InvariantCulture),
            byte[] rowVersion => Convert.ToBase64String(rowVersion),
            var other => throw new InvalidOperationException($"Unexpected concurrency token {other?.GetType().Name}"),
        };

    /// <summary>The save compares against the version the client loaded, not the one loaded just now.</summary>
    public static void Expect(DbContext db, object entity, string token)
    {
        var property = db.Entry(entity).Property(VisitConfiguration.Version);
        property.OriginalValue = property.Metadata.ClrType == typeof(uint)
            ? uint.Parse(token, CultureInfo.InvariantCulture)
            : Convert.FromBase64String(token);
    }
}
```

```csharp
var visit = await db.Visits.SingleAsync(v => v.Id == id, ct);
VersionToken.Expect(db, visit, request.Version);   // the version the client loaded, not the one loaded just now
visit.Amend(request.Notes);
await db.SaveChangesAsync(ct);                     // DbUpdateConcurrencyException if someone saved in between
```

Without `Expect`, the check only covers the milliseconds between this request's load and save; two
admins with the form open for minutes overwrite each other silently.

Tested: two requests edit the same visit; the second `SaveChangesAsync` throws
`DbUpdateConcurrencyException`. Return that as a conflict (409, a Result) and let the user reload.
Don't catch it, reload and save again: that overwrites the first user's change without telling anyone.

## 9. Reads

- `AsNoTracking()` and a projection (`Select` into a DTO) for every read. Lists never return whole
  patient entities.
- Keyset paging (`WHERE id > @after ORDER BY id`), not `Skip`/`Take`, on anything that grows
  (`rules/performance.md`).
- `AsSplitQuery()` when including two or more collections; `Include` chains no deeper than two levels.
- Compiled queries (`EF.CompileAsyncQuery`) only for a measured hot path. Reports and complex reads go to
  Dapper.
- Slow queries show up as database spans in Elastic APM (`observability`); no logging interceptor needed.

## 10. Migrations

Reviewed scripts, never `Migrate()` (`rules/efcore-rules.md`). `/migrate` generates the up and down pair.
- **Additive first.** A new column is nullable or has a default, so the old pods keep working during the
  rollout. Drop or rename in a later release.
- **Bounded types in hand-written migration code too**: `type: "varchar(500)"`, never `type: "text"`.
- **New index on an existing PostgreSQL table:** `IsCreatedConcurrently()`, and script that migration on
  its own without `--idempotent` (`postgresql-patterns` has the reason and the open bug).
- No data fixes inside a schema migration; a separate reviewed script. `HasData` only for small fixed
  lookup lists.

## 11. Review checklist (used by `dba-reviewer`)
- A query filter that reads a local, a constructor value or a service instead of a context member.
- A scoped service (current user, tenant) in a pooled context's constructor or in its interceptors.
- `IDbContextFactory<T>` injected where the context carries per-request state.
- Events published from a `SaveChanges` interceptor instead of the outbox.
- `ExecuteUpdate`/`ExecuteDelete` where audit columns or a concurrency token matter, with neither set.
- `BeginTransaction` with retries enabled and no execution strategy; entities created outside the retried unit.
- A concurrency token whose original value isn't the version the client sent.
- `DbUpdateConcurrencyException` caught and retried by reloading.
- A string without `HasMaxLength`, `type: "text"` in a migration, a `string[]` without an element type.
- `Skip`/`Take` on a growing table; a tracked query on a read path.
