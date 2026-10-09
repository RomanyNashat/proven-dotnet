---
name: migrate
description: "EF Core migration management. Add new migrations, generate SQL scripts (full, range, rollback), and review for safety. Usage: /migrate add <Name>, /migrate script, /migrate script <From> <To>"
allowed-tools: Read, Write, Edit, Bash, Grep, Glob, Agent
---

## Migration Commands

### Add a new migration
```
/migrate add <MigrationName>
```

1. Generate the migration:
   ```bash
   dotnet ef migrations add <MigrationName> -p Infrastructure -s Api
   ```
2. Review the generated migration — delegate to **dba-reviewer** agent:
   - Is it backward-compatible? (N-1 deployment: old code must work with new schema)
   - New columns must be nullable or have defaults
   - No destructive operations (DROP COLUMN/TABLE) without a data migration plan
   - Index creation: use `CONCURRENTLY` (PostgreSQL) or `ONLINE` (SQL Server)
   - Estimated lock time for large tables

### PostgreSQL: a migration with a concurrent index is scripted on its own
New indexes on existing PostgreSQL tables use `IsCreatedConcurrently()` (a plain `CREATE INDEX` blocks
writes). **`--idempotent` breaks these**: EF wraps the statement in a `DO` block and PostgreSQL rejects
`CREATE INDEX CONCURRENTLY` there (npgsql/efcore.pg #3921, open at 10.0.3). So:
1. Keep the concurrent index in **its own migration** — nothing else in it.
2. Script that migration as a range **without** `--idempotent`:
   ```bash
   dotnet ef migrations script <PreviousMigration> <IndexMigration> -p Infrastructure -s Api -o migrations/<IndexMigration>-UP.sql
   ```
3. Say in the MR that this script runs outside a transaction and once only. Every other migration keeps
   the idempotent script below.

Every PostgreSQL script starts with `SET lock_timeout = '5s';` so DDL waiting behind a long transaction
fails fast instead of queuing all traffic behind it.

### Generate full idempotent script (all migrations, safe to re-run)
```
/migrate script
```

```bash
dotnet ef migrations script --idempotent -p Infrastructure -s Api -o migrations/full-idempotent.sql
```

### Generate range script with auto-rollback (FROM → TO, both UP and DOWN)
```
/migrate script <FromMigration> <ToMigration>
```

This generates BOTH the forward (UP) and rollback (DOWN) scripts in one shot:

```bash
# Create output directory
mkdir -p migrations

# UP script: FromMigration → ToMigration (forward)
dotnet ef migrations script <FromMigration> <ToMigration> \
  -p Infrastructure -s Api \
  --idempotent \
  -o migrations/<FromMigration>-to-<ToMigration>-UP.sql

# DOWN script: ToMigration → FromMigration (rollback — reverse direction)
dotnet ef migrations script <ToMigration> <FromMigration> \
  -p Infrastructure -s Api \
  -o migrations/<ToMigration>-to-<FromMigration>-DOWN.sql
```

**Always generates both.** Every migration should ship with its rollback.

### Generate script from scratch to a specific migration
```
/migrate script 0 <ToMigration>
```

```bash
dotnet ef migrations script 0 <ToMigration> \
  -p Infrastructure -s Api \
  --idempotent \
  -o migrations/from-scratch-to-<ToMigration>.sql
```

### Generate script from a specific migration to latest
```
/migrate script <FromMigration>
```

```bash
dotnet ef migrations script <FromMigration> \
  -p Infrastructure -s Api \
  --idempotent \
  -o migrations/<FromMigration>-to-latest.sql
```

### List all migrations
```
/migrate list
```

```bash
dotnet ef migrations list -p Infrastructure -s Api
```

### Remove the last migration (if not yet applied)
```
/migrate remove
```

```bash
dotnet ef migrations remove -p Infrastructure -s Api
```

## After Generating Scripts

1. **Review the SQL** — open the generated `.sql` files and verify the statements
2. **Delegate to dba-reviewer** for safety assessment:
   - N-1 deployment compatibility
   - Lock duration on large tables
   - Index creation strategy (concurrent/online)
   - Data loss risk assessment
3. **Test against a copy** of production data before deploying
4. **Commit both UP and DOWN scripts** alongside the C# migration files

## Reference
- `skills/efcore-patterns/` for EF Core migration best practices
- `skills/postgresql-patterns/` for PostgreSQL-specific migration patterns (CONCURRENTLY, lock_timeout, NOT VALID)
- `skills/sqlserver-patterns/` for SQL Server-specific patterns (ONLINE rebuild)
