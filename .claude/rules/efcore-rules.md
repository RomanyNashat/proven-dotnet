# EF Core Rules

> Reference material for day-to-day work moved to the `efcore-patterns` skill (loaded on demand).
> What stays here is the non-negotiable part: hard bans and anti-patterns.


## Migrations — apply as reviewed SCRIPTS, never by the app (HARD BAN)
- **NEVER call `context.Database.Migrate()` / `MigrateAsync()` / `EnsureCreated()` in application
  code — anywhere, including startup, dev, and local.** This is non-negotiable. The running app must
  never mutate the schema.
- Migrations are applied as **reviewed SQL scripts**, run by the pipeline or a DBA in a controlled
  window — not by the app on boot. Why: multi-pod services race to migrate on startup (partial
  migrations, deadlocks, no rollback); a reviewed script is deployable, auditable, and reversible.
- **One path for everyone** — dev and local also use the script path. No special-casing, no dev
  exception.
- Generate the **up** and **down** scripts as a pair (see the EF skill's migration workflow); commit
  both; hand them to the pipeline or the DBA.


## Column types (SQL Server AND PostgreSQL)

The same rules on both engines. A team with its own DBA rules follows those.

**Strings are bounded (HARD BAN on unbounded text).**
- **Never** `nvarchar(max)` / `varchar(max)` (SQL Server) or `text` / unbounded `varchar` (PostgreSQL).
  Not "just in case", not for a payload you control.
- **Every string property gets an explicit `HasMaxLength(n)`** — plus `IsUnicode(true/false)` on SQL
  Server (no effect on PostgreSQL, where `varchar(n)` is always Unicode). **A property with no length
  configured IS an unbounded column — silence is the bug.** EF Core defaults an unconfigured string to
  `nvarchar(max)` / `text`, so the mistake is made by omission.
- **4,000 characters is the ceiling** on both engines. A value that can exceed it is one of the
  shapes below.
- When a payload feels like it needs more, model it properly:
  1. **Columns** — a known set of fields: each its own bounded column, sized to its source.
  2. **Rows** — a list: a child table with bounded columns.
  3. **Outside the database** — large or free-form blobs go to object storage; the row keeps the key.
  4. **PostgreSQL only: `jsonb` is allowed** for genuinely semi-structured data. Fields
     you filter or join on still belong in real columns. SQL Server JSON stays under the no-`(max)` ban.
- **Raising a limit later is cheap** — a reviewed `ALTER` script. Shipping unbounded is not reversible
  in practice. When in doubt, size it smaller.

**No binary data columns.** Never `varbinary` / `binary` / `image` (SQL Server) or `bytea`
(PostgreSQL) for data: files, images, documents, blobs. They go to object storage and the row keeps a
bounded key. (A concurrency token is not data: `xmin` on PostgreSQL is a system column; SQL Server
`rowversion` is the engine's own mechanism.)

**Keys are `int` identity** (unless your team's rules say otherwise).
- Primary keys are `int` identity: `GENERATED ALWAYS AS IDENTITY` on PostgreSQL (never `serial`),
  `IDENTITY` on SQL Server.
- **`bigint`** only for tables that can realistically pass ~2 billion rows (logs, events, audit).
- **GUIDs are kept to a minimum** — only for an ID created outside the database (an external system's
  ID, a client-generated idempotency key). Stored as native `uuid` / `uniqueidentifier`, never as a
  string column. Do not add a GUID "public id" beside an int key.
- Why `int` by default: small, ordered, index-friendly. A team that prefers GUID keys uses
  `Guid.CreateVersion7()` (time-ordered); random v4 GUIDs fragment clustered indexes.
- If the domain needs the ID before `SaveChanges` (e.g. to put it in a domain event), use a HiLo
  sequence (`UseHiLo()`) — still `int`/`bigint`, no GUID.
- `int` IDs appear in APIs. Every lookup by ID must check the caller may see that record — see
  `rules/security.md`.

**Types are stated explicitly.** Every entity configuration states column types: timestamps are
`datetime2(3)` on SQL Server and `timestamptz` on PostgreSQL (never `timestamp` without time zone —
store UTC). No `money` type; use `decimal(p,s)` / `numeric(p,s)`.

**Exception path (narrow — a decision, not a default):** an unbounded or binary column needs an
explicit, recorded team decision (with the DBA, if there is one) naming the column and why. "It's
easier" is not a justification.

Same rules for hand-written DDL scripts. (`DECLARE @Sql NVARCHAR(MAX)` for dynamic SQL inside a
script is a local variable, not a column — that is fine.)


## Anti-Patterns (NEVER)
- **`Database.Migrate()` / `MigrateAsync()` / `EnsureCreated()` in app code** — banned (see above).
- **Unbounded columns (`nvarchar(max)`, `text` and friends), a string property with no `HasMaxLength(n)`,
  or a binary data column** — banned (see above).
- **Random (v4) GUID primary keys** — keys are `int` identity by default, GUID v7 if your team uses GUIDs (see above).
- No lazy loading — disable with `UseLazyLoadingProxies(false)` (it's off by default, keep it off).
- No `Include()` chains deeper than 2 levels — use projections instead.
- No `ToListAsync()` before `Where()` — filter at database level, not in memory.
- No raw SQL concatenation — always use `FromSql()` with `FormattableString`.
- No `SaveChanges()` in a loop — batch operations or use `ExecuteUpdateAsync`.
- No EF InMemory provider for tests — use Testcontainers with real PostgreSQL/SQL Server.
