# EF Core Rules

> Day-to-day patterns are in the `efcore-patterns` skill. These are the hard bans.

## Migrations: reviewed scripts, never applied by the app (HARD BAN)
- **NEVER call `context.Database.Migrate()`, `MigrateAsync()` or `EnsureCreated()` in application
  code: anywhere, including start-up, dev and local.** The running app never changes the schema; pods
  race to migrate on start-up and a script can be reviewed and rolled back.
- Migrations ship as reviewed SQL scripts, run by the pipeline or a DBA. The same path for dev and local.
- Generate the up and down scripts as a pair and commit both (`efcore-patterns`).

## Column types (SQL Server and PostgreSQL)
A team with its own DBA rules follows those.
- **Strings are bounded (HARD BAN on unbounded text).** Never `nvarchar(max)` / `varchar(max)`, or
  `text` / unbounded `varchar`. **Every string property gets `HasMaxLength(n)`** (plus `IsUnicode` on SQL
  Server): EF Core makes an unconfigured string unbounded, so leaving it out is the bug.
- **4,000 characters is the ceiling.** A value that can be longer is modelled another way (`efcore-patterns`).
- **No binary data columns:** never `varbinary` / `binary` / `image` or `bytea` for files or blobs.
  They go to object storage and the row keeps a bounded key. (`xmin` and `rowversion` are concurrency
  tokens, not data.)
- **Keys are `int` identity** (unless your team's rules say otherwise): `GENERATED ALWAYS AS IDENTITY`
  on PostgreSQL (never `serial`), `IDENTITY` on SQL Server; `bigint` only for a table that can pass
  about 2 billion rows. A GUID only for an id made outside the database, stored as `uuid` /
  `uniqueidentifier`, never as a string, and never a GUID "public id" beside an int key. A team on GUID
  keys uses `Guid.CreateVersion7()`. `int` ids in an API mean every lookup checks ownership
  (`rules/security.md`).
- **Types are stated:** timestamps `datetime2(3)` on SQL Server, `timestamptz` on PostgreSQL (UTC, never
  `timestamp` without time zone); money `decimal(p,s)` / `numeric(p,s)`, never `money`.
- **The exception** for an unbounded or binary column is a recorded team decision (with the DBA, if
  there is one) naming the column and why. "It's easier" is not a reason.
- The same rules for hand-written DDL. (`DECLARE @Sql NVARCHAR(MAX)` in a script is a variable, not a column.)

## Anti-patterns (never)
- Lazy loading (`UseLazyLoadingProxies`): it's off by default; keep it off.
- `Include()` chains deeper than 2 levels: use a projection.
- `ToListAsync()` before `Where()`: filter in the database.
- Raw SQL concatenation: `FromSql()` with a `FormattableString`.
- `SaveChanges()` in a loop: batch, or `ExecuteUpdateAsync` / `ExecuteDeleteAsync`.
- The EF InMemory provider in tests: Testcontainers with the real engine.
