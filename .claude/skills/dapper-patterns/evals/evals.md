# dapper-patterns — evals

Mechanical checks only (see `skill-evals`). Written when the skill was rewritten (v2.0.0) to A/B the old
version against the new one. Each case targets a defect found in the old version or a dialect trap.

### Case 1: a paged read with cancellation
**Prompt:** Write a Dapper repository method `GetCustomerOrdersAsync(int customerId, int afterId, int size, CancellationToken ct)` for PostgreSQL. Show the code.
**Must:** the token reaches Dapper (`CommandDefinition(..., cancellationToken: ct)`); keyset paging
(`id > @afterId`); named columns.
**Must not:** `QueryAsync(sql, new { … })` with the token unused; `OFFSET`; `SELECT *`.

### Case 2: sorting chosen by the caller
**Prompt:** The orders list endpoint takes `sortBy` (a column name) and `desc` from the query string. Add the sorting to the Dapper query. Show the code.
**Must:** an allowlist mapping `sortBy` to a fixed column; anything else refused or defaulted.
**Must not:** the `sortBy` string put into the SQL text.

### Case 3: thousands of ids on SQL Server
**Prompt:** On SQL Server, load the orders for a list of ids that can hold up to 5,000 ids. Use Dapper. Show the code.
**Must:** one parameter for the list (OPENJSON, a TVP, or `STRING_SPLIT`), or batches under 2,100; the
2,100-parameter limit named.
**Must not:** `WHERE id IN @ids` with the whole list.

### Case 4: inserting 10,000 rows
**Prompt:** Insert 10,000 audit rows with Dapper on PostgreSQL. Show the code.
**Must:** one command or a bulk path (`unnest`, `COPY`/binary importer, or multi-row batches).
**Must not:** `ExecuteAsync(sql, listOfRows)` presented as a batch.

### Case 5: the same query on SQL Server
**Prompt:** This PostgreSQL Dapper query must also run in our SQL Server service: `SELECT id, total FROM orders WHERE customer_id = @c ORDER BY id LIMIT 20`, plus an insert that returns the new id with `RETURNING id`. Show the SQL Server versions.
**Must:** `TOP (20)` (or `OFFSET … FETCH`) and `OUTPUT INSERTED.id`.
**Must not:** `LIMIT` or `RETURNING` left in the SQL Server version.

### Case 6: the SQL Server connection
**Prompt:** Register and use a SQL Server connection for Dapper in an ASP.NET Core service. Show the code.
**Must:** `Microsoft.Data.SqlClient`; a connection opened per operation (or a factory).
**Must not:** `System.Data.SqlClient`; one connection shared between concurrent operations.

## Results

### 2026-10-06 — A/B, old skill (v1, 11.1 KB) vs rewrite (v2.0.0, 9.1 KB)

One fresh subagent per case and version (12 runs).

| Case | Old skill | New skill |
|---|---|---|
| 1 paged read | pass; the agent switched the skill's `OFFSET` to keyset and added the `CommandDefinition` the skill never shows | pass |
| 2 caller-chosen sort | pass; allowlist, which the agent said the skill doesn't cover | pass |
| 3 5,000 ids on SQL Server | pass; a TVP, "the skill doesn't cover this case" | pass: OPENJSON, TVP as the fallback |
| 4 10,000 rows | pass; `COPY`, and the agent called the skill's "Dapper auto-batches" wrong | pass: `unnest`, `COPY` past 100k |
| 5 the SQL Server dialect | pass (`TOP`, `OUTPUT INSERTED`), with the agent noting the skill is PostgreSQL only | pass |
| 6 SQL Server connection | pass; the agent rejected the skill's scoped `SqlConnection` | pass |

Old 6/6, new 6/6. The fourth reference skill with this result: the agents caught or worked around every
defect, and named it in five of the six old-skill answers. What the rewrite adds is code that runs on both
engines in CI (11 tests, including SQL Server refusing `IN @ids` above 2,100 and a cancelled token
stopping a 20-second query) and no wrong examples to argue with. About 2.4k fewer tokens per run.

