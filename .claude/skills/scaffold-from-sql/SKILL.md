---
name: scaffold-from-sql
description: /scaffold mode generating a .NET service layer FROM existing SQL (schema, stored procs, .sql): Dapper DTOs, repositories, services, endpoints.
---

# Scaffold from SQL — generate the service layer from existing SQL

The SQL-first mode of `/scaffold`. When a service is built around an existing database — a table
schema, a set of stored procedures, or a `.sql` script — this generates the .NET data-access layer to
match: Dapper DTOs, repositories, service classes, and Minimal API endpoints. It reads the SQL as the
source of truth and shapes the code to fit, rather than starting from a C# model.

Uses **Dapper** (per `dapper-patterns`) for data access, since SQL-first services want direct control
over the queries. Everything is generated in Plan Mode and presented before writing.

## Question 1 — what SQL do you have?
- **A table schema** (`CREATE TABLE …`) → generate a DTO per table, CRUD repository, service, endpoints.
- **Stored procedures** → a repository method per procedure (parameters → method args, result set →
  DTO), plus service + endpoints that call them. The engines differ here:
  - **SQL Server:** `CREATE PROCEDURE` returns result sets; call it with `CommandType.StoredProcedure`,
    and handle `OUTPUT` parameters and the return value explicitly.
  - **PostgreSQL:** rows come from a **function** (`CREATE FUNCTION … RETURNS TABLE (…)` or `SETOF`),
    called as `SELECT * FROM schema.fn(@a, @b)` with `CommandType.Text`. A PostgreSQL `PROCEDURE` returns
    no rows (only `OUT`/`INOUT` values), and `CommandType.StoredProcedure` runs `CALL` (Npgsql 7+), so
    it can't call a function. See `dapper-patterns` §2.
- **A SQL script / `.sql` file** → parse whichever of the above it contains and generate accordingly.
- **A live schema/context** → if a `.sql` isn't handy, ask the developer to paste the `CREATE TABLE` /
  `CREATE PROCEDURE` text, or point at the file.

Never guess the schema — read it from what the developer provides.

## What it generates (matched to the SQL)
1. **DTOs** — one record per table or per SP result set. Column → property with the correct C# type
   (map SQL types: `nvarchar`→`string`, `int`→`int`, `bit`→`bool`, `datetime2`→`DateTime`,
   `decimal`→`decimal`, `uniqueidentifier`→`Guid`, nullable columns → nullable C# types; PostgreSQL:
   `varchar`→`string`, `integer`→`int`, `bigint`→`long`, `boolean`→`bool`, `timestamptz`→`DateTime` (UTC),
   `numeric`→`decimal`, `uuid`→`Guid`, `jsonb`→a typed class). PascalCase
   the property names; keep the real column names for the Dapper mapping.
2. **Repository** — Dapper-based, one interface + implementation. For a table: `GetByIdAsync`,
   `GetAllAsync`, `InsertAsync`, `UpdateAsync`, `DeleteAsync` with **parameterized** queries. For SPs:
   one method per procedure, `CommandType.StoredProcedure`, parameters bound by name.
3. **Service class** — thin layer over the repository (interface + implementation), where any business
   logic lives (keep controllers/endpoints thin per SHY003).
4. **Endpoints** — Minimal API group (or controllers if the developer prefers), one endpoint per
   repository operation, with `TypedResults`, validation, and `ProducesResponseType`.
5. **DI wiring** — register the repository + service; connection string from configuration. The
   connection follows `dapper-patterns` §3: one `NpgsqlDataSource` (`AddNpgsqlDataSource`) on PostgreSQL,
   a new `Microsoft.Data.SqlClient.SqlConnection` per operation on SQL Server.

## Correctness rules
- **Parameterized queries always** — never interpolate values into SQL (SHY101). SP calls bind
  parameters by name.
- **Match the SQL exactly** — column names, types, nullability. A nullable column maps to a nullable
  C# type; don't silently make it non-null.
- **SPs:** parameters → method arguments in the same order/names; result set → the generated DTO.
  SQL Server: `OUTPUT` params and return values handled explicitly. PostgreSQL: functions through
  `SELECT * FROM fn(…)`, procedures through `CALL` with `OUT`/`INOUT` params.
- **Async all the way** — `QueryAsync`/`ExecuteAsync`, methods suffixed `Async`.
- **Thin endpoints** — logic in the service, not the endpoint (SHY003).
- Follow the zero-vulns + security rules for the generated code (no hardcoded connection strings).

## Parity
This is a **feature-shaped** generation (new code), so under `remediation-parity` it's Tier-2:
after generating, build and run the tests to confirm nothing unrelated broke and the new code compiles.
It's not a behavior-preserving refactor, so no characterization/parity snapshot is needed.

## Rules
- Read the SQL as the source of truth; ask what SQL the developer has before generating.
- Dapper for data access; parameterized queries only; match column names/types/nullability exactly.
- Plan Mode — present the generated layer before writing; the developer approves and applies.
- Thin endpoints, logic in services, async throughout, no hardcoded secrets.

## Column sizing in generated code
The source SQL already declares real lengths — **carry them through**. Every generated entity
configuration gets an explicit `HasMaxLength(n)` matching the source column (plus `IsUnicode(...)` on SQL
Server: `nvarchar` → `true`, `varchar` → `false`; Arabic text is always `nvarchar`, see `localization`
§8), and an explicit `HasColumnType(...)` for dates/decimals. Never emit an unconfigured string property
(that becomes `nvarchar(max)` on SQL Server and `text` on PostgreSQL), and never mirror a source `MAX` column into a new one — flag it instead and
propose one of the three proper shapes. See `rules/efcore-rules.md` (HARD BAN on MAX).
