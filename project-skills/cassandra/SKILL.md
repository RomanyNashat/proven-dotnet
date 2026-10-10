---
name: cassandra
description: Apache Cassandra for .NET with the DataStax C# driver — query-first data modeling, partition-key design, wide rows, and tunable consistency. A per-project skill: drop it into a repo's .claude/skills/ only for services that actually use Cassandra.
---

# Cassandra — query-first modeling for .NET

A distributed wide-column store. The whole mental model is **inverted from a relational DB**: you do
not model your entities and then query them — you model your *queries* and shape tables to serve them.
Getting the partition key wrong is the mistake that doesn't show up until production scale.

> Per-project skill. Only load this in a service that uses Cassandra (copy the folder into that repo's
> `.claude/skills/`). It's kept out of the global set so it doesn't compete for skill-selection budget.

## Query-first data modeling (the core discipline)
- **Start from the read patterns**, not the entities. List every query the service must answer, then
  design one table per query shape. Denormalize freely — the same data living in multiple tables,
  each keyed for a specific query, is normal and correct here.
- There are **no joins** and no ad-hoc `WHERE` on arbitrary columns. If a table can't answer a query
  from its key, you need a different table (or a materialized view).

## Partition key — the decision that matters most
The primary key is `(partition_key, clustering_columns)`.
- **Partition key** decides which node holds the data. It must (a) spread data evenly across the
  cluster and (b) be present in every query against that table (queries hit one partition).
- **Bad partition keys:** low-cardinality (e.g. a status with 3 values → 3 giant hot partitions), or
  monotonic (a date alone → today's partition is a hotspot). 
- **Good:** high-cardinality and query-aligned (e.g. `patient_id`), or a composite that bounds
  partition size (`(patient_id, year_month)`) so no single partition grows unbounded.
- **Clustering columns** sort rows within a partition — that's how you get ordered ranges (e.g. events
  newest-first) cheaply.

## Wide rows
A partition can hold many rows (a "wide row") — e.g. all events for one patient under `patient_id`,
sorted by time. Great for time-series/feed reads. But **bound the partition size** (add a time bucket
to the key) so a hot entity doesn't create a multi-GB partition that degrades the node.

## Tunable consistency
Per-query, you trade consistency vs. availability/latency by choosing consistency levels:
- `ONE` / `LOCAL_ONE` — fast, may read slightly stale. 
- `QUORUM` / `LOCAL_QUORUM` — majority of replicas; the usual production default for correctness.
- Read CL + Write CL relationship: `R + W > replication factor` gives read-your-writes consistency.
Pick per operation — reference reads can be `ONE`; anything correctness-sensitive is `LOCAL_QUORUM`.

## The .NET driver (CassandraCSharpDriver / DataStax)
- One `ISession` per keyspace, long-lived, shared — it manages the connection pool. Never per-query.
- **Always use prepared statements** for repeated queries — parse once, bind many; faster and safe
  from injection. `var ps = session.Prepare("SELECT ... WHERE patient_id = ?");`
- Bind and execute with the right consistency level per statement.
- Use `IAsyncEnumerable`/paging for large result sets; don't materialize huge partitions at once.

## Rules
- Model tables per query, not per entity — denormalize on purpose.
- Partition key: high-cardinality, query-aligned, size-bounded (add a time bucket for wide rows).
- No joins, no arbitrary WHERE — if the key can't serve the query, make another table.
- Prepared statements always; one long-lived session; pick consistency level per operation.
