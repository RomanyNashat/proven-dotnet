# Performance Standards

- Async all the way, endpoint to database: no sync-over-async, no async-over-sync, no `.Result` or `.Wait()`.
- A `CancellationToken` on every async method that touches I/O, passed all the way through.
- `Task.WhenAll()` for independent I/O, not one await after another.
- `IHttpClientFactory` or typed clients (pooled handlers that follow DNS changes), with a timeout on every
  call; never an infinite default.
- Size the database pool for the fleet: `pods × Maximum Pool Size` fits under the server's
  `max_connections` (PostgreSQL defaults to 100).
- Keyset (cursor) paging on anything that grows, not offset.
- Every cache entry has an absolute expiration.
- Index every column used in WHERE, JOIN and ORDER BY, and check the plan.
- Profile before optimizing (BenchmarkDotNet with `[MemoryDiagnoser]`).

The rest is in the skills: hot paths and allocation in `dotnet-core`, EF Core and Dapper reads in
`efcore-patterns` and `dapper-patterns`, caches in `caching`, HTTP resilience in `polly-resilience`,
compression and cache headers in `api-design`.
