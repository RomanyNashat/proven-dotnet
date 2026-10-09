# ddd-patterns — evals

Mechanical checks only (see `skill-evals`). Written when the skill was rewritten (v2.0.0) to A/B the old
version against the new one. Each case targets a defect found in the old version.

### Case 1: changing an existing aggregate
**Prompt:** Write the application handler that adds a product line to an existing order (`Order.AddLine(...)` recalculates `Total` from the lines). Show the handler and the repository method it uses.
**Must:** the order is loaded with its lines (an `Include`, or a repository method that returns the whole
aggregate).
**Must not:** `FindAsync`/a lookup without the lines before calling `AddLine`; `Update(order)` on the
tracked aggregate.

### Case 2: an event another service needs
**Prompt:** When an order is placed, the Inventory service (another microservice) must reserve stock. Show how the `OrderPlaced` domain event gets there.
**Must:** through the outbox (written in the same transaction, relayed to Kafka).
**Must not:** a domain-event handler that calls a Kafka producer directly.

### Case 3: an entity base class
**Prompt:** Write an `Entity` base class for our domain model. Keys are `int` identity.
**Must:** no equality on `Id` alone (keep reference equality, or treat id 0 as "not equal to anything else").
**Must not:** `Equals`/`GetHashCode` that make two unsaved entities equal.

### Case 4: the id inside the creation event
**Prompt:** `Order.Place(...)` raises `OrderPlaced(OrderId, ...)`. Keys are `int`. Make sure the event carries the real id. Show the mapping and how the id gets there.
**Must:** an `int` id known before the event is written (HiLo or a sequence, or events built when they're
collected).
**Must not:** a GUID key or GUID public id; an event that can carry 0.

### Case 5: mapping an address
**Prompt:** Map an `Address` value object (Street, City, PostalCode, Country) on `Customer` with EF Core. Show the configuration.
**Must:** `HasMaxLength` on every string property.
**Must not:** a string property left without a length.

### Case 6: the event base type
**Prompt:** Write the base record for our domain events, with an event id and the time it happened.
**Must:** the time from an injected `TimeProvider` (or set by infrastructure when the event is stored).
**Must not:** `DateTime.UtcNow` / `DateTimeOffset.UtcNow` / `DateTime.Now` in the base record.

## Results

### 2026-10-05 — A/B, old skill (v1, 15.3 KB) vs rewrite (v2.0.0, 9.9 KB)

One fresh subagent per case and version (12 runs; cases 5 and 6 were rerun after a usage limit cut the
first attempt short).

| Case | Old skill | New skill |
|---|---|---|
| 1 changing an aggregate | pass; the agent refused the skill's `FindAsync` repository and its `Update` | pass |
| 2 event to another service | pass; outbox, and the agent called the skill's Kafka-in-a-handler a dual write | pass |
| 3 entity base class | pass; the agent fixed the skill's id-0 equality (and type equality) | pass: no `Equals` on `Id` at all |
| 4 id inside the creation event | pass, with the skill's sequence generator (and it said HiLo can't work, true without deferred events) | pass: HiLo plus an event factory |
| 5 mapping an address | pass; the agent added the lengths the skill's `OwnsOne` example left out | pass |
| 6 event base type | pass; the agent removed the skill's `DateTimeOffset.UtcNow` | pass |

Old 6/6, new 6/6. Same result as `efcore-patterns`: the agents caught every planted defect from the
always-loaded rules, and said so in five of the six old-skill answers. The rewrite removes examples they
had to argue against and adds a tested trap (§2: a partial load silently saves a wrong total). About
1.4k fewer tokens per load from the size.

