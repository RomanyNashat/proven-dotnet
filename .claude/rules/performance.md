# Performance Standards

## Async Patterns
- Async all the way — from controller/endpoint to database. No sync-over-async, no async-over-sync.
- Use `ConfigureAwait(false)` in library code (not in ASP.NET Core controllers — context flows automatically).
- Use `ValueTask<T>` for hot paths that frequently complete synchronously (cache hits, pre-computed results).
- Prefer `Task.WhenAll()` for independent concurrent operations over sequential awaits.
- Use `CancellationToken` on every async method that touches I/O. Pass it all the way through.
- Use `Channel<T>` for producer-consumer patterns — not `BlockingCollection<T>`.

## Allocation Awareness
- Use `Span<T>` and `ReadOnlySpan<T>` for parsing, slicing, and buffer manipulation on hot paths.
- Use `ArrayPool<T>.Shared` or `MemoryPool<T>` for temporary buffers instead of allocating arrays.
- Use `stackalloc` for small, fixed-size buffers (< 256 bytes) in performance-critical code.
- Use `StringBuilder` for string concatenation in loops (not string interpolation in loops).
- Use `string.Create()` for building strings without intermediate allocations.
- Use `StringComparison.OrdinalIgnoreCase` — not `ToLower()`/`ToUpper()` for comparisons.
- Avoid boxing: don't pass value types as `object`. Use generic interfaces.
- Avoid LINQ in tight loops — `for` loop with direct indexing can be 5-10x faster.
- Profile before optimizing. Use BenchmarkDotNet with `[MemoryDiagnoser]` to measure.

## Database Performance
- EF Core `AsNoTracking()` for ALL read-only queries — reduces memory and CPU.
- EF Core compiled queries for hot paths — 10-40% improvement on repeated queries.
- EF Core `AsSplitQuery()` for multi-collection includes — avoids cartesian explosion.
- EF Core `ExecuteUpdateAsync()`/`ExecuteDeleteAsync()` for bulk operations — 300-500x faster than SaveChanges loop.
- Dapper for complex read queries — no tracking overhead, direct SQL control.
- Connection pooling: DbContext pooling (128 pool size). Size the DB connection pool for the fleet:
  `pods × Maximum Pool Size` must fit under the server's `max_connections` (PostgreSQL defaults to 100).
- Index every column used in WHERE, JOIN, ORDER BY. Verify with EXPLAIN/EXPLAIN ANALYZE.
- Pagination: keyset (cursor-based) over offset for large datasets. Offset degrades linearly.

## Caching Strategy
- IMemoryCache for single-instance, hot, small data (< 100 items, < 1MB)
- IDistributedCache (Redis) for multi-instance, shared state, session data
- Cache-aside pattern: check cache → miss → load from source → store in cache → return
- Always set absolute expiration. Sliding expiration only when access pattern justifies it.
- Cache stampede prevention: use lock/semaphore or `GetOrCreateAsync` with cancellation.

## HTTP Performance
- Response compression: Brotli (preferred) or Gzip via `AddResponseCompression()`
- Response caching headers: `Cache-Control`, `ETag`, `Last-Modified` for cacheable endpoints
- Use `IHttpClientFactory` with `SocketsHttpHandler` — connection pooling and DNS rotation
- Set reasonable timeouts on all HTTP calls. Never rely on infinite defaults.
