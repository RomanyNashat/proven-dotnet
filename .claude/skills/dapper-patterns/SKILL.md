---
name: dapper-patterns
description: Dapper, on PostgreSQL and SQL Server — the token reaches the database, keyset paging, allowlisted sorting, any number of ids in one parameter, many rows in one command, the dialect differences. Core code tested in CI on both engines.
version: 2.0.0
---

# Dapper Patterns

The model knows Dapper. This skill holds what's specific here and the places where the common example
is wrong. The sample is tested in CI against **both** PostgreSQL 17 and SQL Server 2022
(`tests/SkillSamples.Tests/Dapper`): the same tests run on each engine.

Where it fits: EF Core for writes (`efcore-patterns`), Dapper for reads, reports and stored procedures
(`cqrs-eventsourcing`). Engine details: `postgresql-patterns`, `sqlserver-patterns`.

## 1. The read side, on either engine

<!-- sample: tests/SkillSamples.Tests/Dapper/OrderReads.cs -->
```csharp
public enum Engine { PostgreSql, SqlServer }

public sealed record OrderRow(int Id, int CustomerId, decimal Total, DateTime CreatedAt);

public sealed record NewOrder(int CustomerId, decimal Total);

/// <summary>
/// Read side with Dapper, on either engine. Every call goes through a CommandDefinition so the
/// CancellationToken reaches the database; the anonymous-object overloads take no token at all.
/// </summary>
public sealed class OrderReads(Func<CancellationToken, Task<DbConnection>> open, Engine engine)
{
    private const string Columns = "id AS Id, customer_id AS CustomerId, total AS Total, created_at AS CreatedAt";

    // The only sort columns a caller can pick. User input selects a key; it never becomes SQL.
    private static readonly Dictionary<string, string> SortColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["created"] = "created_at",
        ["total"] = "total",
    };

    /// <summary>Keyset paging: the next page after the last id the caller saw.</summary>
    public async Task<IReadOnlyList<OrderRow>> PageAsync(int customerId, int afterId, int size, CancellationToken ct)
    {
        var sql = engine == Engine.PostgreSql
            ? $"SELECT {Columns} FROM orders WHERE customer_id = @customerId AND id > @afterId ORDER BY id LIMIT @size"
            : $"SELECT TOP (@size) {Columns} FROM orders WHERE customer_id = @customerId AND id > @afterId ORDER BY id";
        await using var connection = await open(ct);
        var rows = await connection.QueryAsync<OrderRow>(new CommandDefinition(sql, new { customerId, afterId, size }, cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>A caller-chosen sort, through the allowlist.</summary>
    public async Task<IReadOnlyList<OrderRow>> TopAsync(int customerId, string sort, int size, CancellationToken ct)
    {
        if (!SortColumns.TryGetValue(sort, out var column))
        {
            throw new ArgumentException($"Unknown sort '{sort}'", nameof(sort));
        }

        var sql = engine == Engine.PostgreSql
            ? $"SELECT {Columns} FROM orders WHERE customer_id = @customerId ORDER BY {column} DESC, id DESC LIMIT @size"
            : $"SELECT TOP (@size) {Columns} FROM orders WHERE customer_id = @customerId ORDER BY {column} DESC, id DESC";
        await using var connection = await open(ct);
        var rows = await connection.QueryAsync<OrderRow>(new CommandDefinition(sql, new { customerId, size }, cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>
    /// Any number of ids in one parameter. PostgreSQL takes an int[]; SQL Server takes JSON. Dapper's
    /// `IN @ids` expands to one parameter per id, and SQL Server refuses more than 2,100.
    /// </summary>
    public async Task<IReadOnlyList<OrderRow>> ByIdsAsync(IReadOnlyCollection<int> ids, CancellationToken ct)
    {
        var (sql, args) = engine == Engine.PostgreSql
            ? ($"SELECT {Columns} FROM orders WHERE id = ANY(@ids)", (object)new { ids = ids.ToArray() })
            : ($"SELECT {Columns} FROM orders WHERE id IN (SELECT CAST(value AS int) FROM OPENJSON(@ids))",
               new { ids = JsonSerializer.Serialize(ids) });
        await using var connection = await open(ct);
        var rows = await connection.QueryAsync<OrderRow>(new CommandDefinition(sql, args, cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>
    /// Many rows in one command. Passing a list to ExecuteAsync runs the INSERT once per row.
    /// </summary>
    public async Task<int> InsertManyAsync(IReadOnlyCollection<NewOrder> orders, CancellationToken ct)
    {
        var (sql, args) = engine == Engine.PostgreSql
            ? ("INSERT INTO orders (customer_id, total) SELECT * FROM unnest(@customerIds, @totals)",
               (object)new { customerIds = orders.Select(o => o.CustomerId).ToArray(), totals = orders.Select(o => o.Total).ToArray() })
            : ("""
               INSERT INTO orders (customer_id, total)
               SELECT customer_id, total FROM OPENJSON(@rows) WITH (customer_id int '$.CustomerId', total decimal(18,2) '$.Total')
               """,
               new { rows = JsonSerializer.Serialize(orders) });
        await using var connection = await open(ct);
        return await connection.ExecuteAsync(new CommandDefinition(sql, args, cancellationToken: ct));
    }
}
```

What the tests show, on each engine:
- **The token reaches the database.** A 20-second query is stopped within seconds when the token is
  cancelled. The overloads that take an anonymous object have no token parameter, so a method can accept
  a `CancellationToken` and still never pass it: always go through `CommandDefinition`.
- **Keyset paging** returns every row once, in order, across pages.
- **Sorting from user input goes through an allowlist.** `"total; DROP TABLE orders; --"` is refused
  before any SQL runs. Parameters can't carry a column name, so this is the one place where SQL text is
  built from input, and only from the allowlist's values.
- **3,000 ids in one parameter** work on both engines. Dapper's `IN @ids` expands to one parameter per
  id, and a test shows SQL Server refusing it above 2,100.
- **5,000 rows in one command.** `ExecuteAsync(sql, listOfRows)` looks like a batch but runs the INSERT
  once per row: one round trip each.

## 2. The two dialects

| | PostgreSQL | SQL Server |
|---|---|---|
| Page | `ORDER BY id LIMIT @size` (keyset: `WHERE id > @after`) | `SELECT TOP (@size) … ORDER BY id` (keyset the same) |
| Many ids | `= ANY(@ids)` with an `int[]` | `IN (SELECT CAST(value AS int) FROM OPENJSON(@ids))`, or a TVP |
| Many rows | `unnest(@a, @b)`; `COPY` (binary importer) for 100k+ | `OPENJSON(@rows) WITH (…)`; `SqlBulkCopy` for 100k+ |
| New id back | `INSERT … RETURNING id` | `INSERT … OUTPUT INSERTED.id` |
| Upsert | `INSERT … ON CONFLICT (…) DO UPDATE` | `UPDATE … ; IF @@ROWCOUNT = 0 INSERT …` under `WITH (UPDLOCK, HOLDLOCK)`; plain `MERGE` races |
| Strings | `varchar(n)`, always Unicode | `nvarchar(n)` for Arabic text. Dapper sends strings as `nvarchar`: against a `varchar` column that converts the column and its index isn't used, so pass `new DbString { Value = code, IsAnsi = true, Length = 20 }` |
| Procedures | functions: `SELECT * FROM fn(@a)`; `CommandType.StoredProcedure` runs `CALL` (Npgsql 7+) | `CommandType.StoredProcedure`; don't name them `sp_…` (SQL Server looks in `master` first) |

`SELECT EXISTS(…)`, `LIMIT` and `::type` are PostgreSQL only; `TOP`, `OUTPUT` and `[brackets]` are SQL
Server only. A query written for one engine is not "standard SQL".

## 3. Connections and transactions

- **PostgreSQL:** one `NpgsqlDataSource` (`AddNpgsqlDataSource`), and `OpenConnectionAsync(ct)` per
  operation.
- **SQL Server:** `Microsoft.Data.SqlClient` (not `System.Data.SqlClient`, which is deprecated); a new
  `SqlConnection` per operation, pooled by the driver. Don't register a connection as a long-lived
  service, and never share one between concurrent tasks.
- A transaction goes into the `CommandDefinition` beside the token:
  `new CommandDefinition(sql, args, transaction: tx, cancellationToken: ct)`. `await using` on the
  transaction rolls back anything not committed; no try/catch needed for that.

## 4. Mapping
- Name every column (`SELECT id AS Id, …`); `SELECT *` breaks a record's constructor binding the day a
  column is added, and reads columns the screen doesn't need.
- Multi-mapping: `splitOn` names the first column of the next object; for one-to-many, de-duplicate the
  parents in a dictionary.
- `QueryMultipleAsync` reads several result sets in one round trip, on both engines.

## 5. Review checklist (used by `dba-reviewer`)
- A Dapper call without `CommandDefinition` in a method that has a `CancellationToken`.
- SQL text built from input other than through an allowlist; a sort or column name from the request.
- `IN @ids` where the list can grow past a few hundred (SQL Server refuses 2,100).
- `ExecuteAsync(sql, list)` for more than a handful of rows.
- `OFFSET` paging on a growing table; `SELECT *`.
- PostgreSQL-only syntax in a SQL Server service, or the reverse; `System.Data.SqlClient`.
