# efcore-patterns — evals

Mechanical checks only (see `skill-evals`). Written when the skill was rewritten (v2.0.0) to A/B the old
version against the new one. Each case targets a defect found in the old version.

### Case 1: a tenant filter
**Prompt:** Our `OrdersDbContext` gets an `ITenantService` (scoped, reads the tenant from the token). Add a global query filter so every query only returns the current tenant's orders. Show the context code.
**Must:** the filter reads a member of the context instance (a property or field), set per request.
**Must not:** a value copied into a local (or read from the service) inside `OnModelCreating` and captured
by the filter.

### Case 2: audit columns with a pooled context
**Prompt:** We register `AddDbContextPool<ClinicDbContext>`. Add an interceptor that fills `CreatedBy`/`UpdatedBy` from the current user (`ICurrentUserService`, scoped). Show the interceptor and the registration.
**Must:** the user on each save is the current request's user (read from the context, set per rental, or
the pool dropped for `AddDbContext` with that reason).
**Must not:** a scoped `ICurrentUserService` resolved once into the pool's options or the interceptor.

### Case 3: domain events to Kafka
**Prompt:** When an `Appointment` aggregate is saved, its domain events must reach Kafka. Show how, with EF Core.
**Must:** the outbox: the event row is written in the same transaction as the change, and a relay publishes.
**Must not:** publish to Kafka from a `SaveChanges` interceptor or directly after `SaveChangesAsync`.

### Case 4: a column and an index on a large PostgreSQL table
**Prompt:** Add a nullable `Notes` column (up to 1000 characters) to the `orders` table, and an index on `customer_id`. The table is large and busy, on PostgreSQL. Show the EF configuration and how the migration gets applied.
**Must:** `HasMaxLength(1000)` / `varchar(1000)`; the index created concurrently; applied as a reviewed script.
**Must not:** `text` or an unbounded string; a plain blocking `CREATE INDEX`; `Migrate()` in the app.

### Case 5: two admins edit the same appointment
**Prompt:** Two admins can edit the same appointment at the same time. Handle it with EF Core on PostgreSQL. Show the configuration and what the endpoint does when it happens.
**Must:** a concurrency token (`xmin` / `IsRowVersion`); the conflict goes back to the caller (409).
**Must not:** catch the exception, reload and save again (the second edit silently wins).

### Case 6: a transaction with retries on
**Prompt:** The service uses `EnableRetryOnFailure`. Save an order, then its payment record, in two `SaveChangesAsync` calls that must commit together. Show the code.
**Must:** `CreateExecutionStrategy().ExecuteAsync(...)` around the transaction.
**Must not:** `BeginTransactionAsync` outside the execution strategy.

### Case 7: archiving in bulk
**Prompt:** Mark every visit older than two years as archived, efficiently. `Visit` has `UpdatedAt`/`UpdatedBy` filled by an audit `SaveChangesInterceptor`. Show the code.
**Must:** `ExecuteUpdateAsync`, with `UpdatedAt`/`UpdatedBy` set in it (or an explicit note that the
interceptor won't run).
**Must not:** load and save in a loop; rely on the interceptor for the bulk update.

## Results

### 2026-10-05 — A/B, old skill (v1, 18.5 KB) vs rewrite (v2.0.0)

One fresh subagent per case and version (14 runs), graded against the lines above.

| Case | Old skill | New skill |
|---|---|---|
| 1 tenant filter | pass; the agent called the skill's filter (a local captured in `OnModelCreating`) wrong and didn't copy it | pass |
| 2 audit with a pool | pass; the agent called the skill's interceptor a captive dependency and rebuilt it | pass |
| 3 events to Kafka | pass; outbox, and the agent named the skill's "dispatch after save" as running before the save | pass |
| 4 column and index | pass; the agent replaced the skill's `type: "text"` and added the missing transaction handling | pass |
| 5 concurrent edits | pass | pass |
| 6 transaction with retries | pass | pass |
| 7 bulk archive | pass; the agent noted the skill never says `ExecuteUpdate` skips interceptors | pass |

Old 7/7, new 7/7. The agents caught every defect in the old skill from the always-loaded rules and their
own knowledge, and said the skill was wrong in five of the seven answers. So on these cases the rewrite
doesn't change the grade: it removes wrong examples that the agent had to argue against, and the core code
is now tested. Size 18.5 → 15.7 KB, about 0.7k tokens less per load.

**Changed after the results** (disclosed; no case was changed):
- §8 now shows setting the concurrency token's original value to the version the client sent. Both case 5
  runs did this and the skill didn't say it; without it, the check covers one request, not two admins.
  Tested.
- §7's helper was rewritten. The new-skill case 6 run found that the first version (one final save with
  `acceptAllChangesOnSuccess: false`) didn't fit two dependent saves, though the text said it was for that.
  The helper now clears the tracker and runs the whole unit on each attempt; a test fails the second of
  two saves once and checks the retry commits both rows once.
