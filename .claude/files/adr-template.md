# ADR Template — Lightweight Decision Record

> **Purpose:** This is the lightweight ADR format used by proven-dotnet (the handover command reads them).
> Claude Code creates these automatically during technical decision conversations.
> They live in `docs/decisions/` (or wherever your repo keeps decision records) within each project repo.
>
> **Lifecycle:** Created when a decision discussion starts → updated as it evolves →
> **deleted when the work is completed and merged** (the code speaks for itself).
> Any ADR still present when `/handover` runs = in-flight work with unfinished decisions.
>
> **This is NOT the same as the architect agent's formal ADR format.**
> The architect agent creates permanent architectural records (`ADR-[NNN]`).
> These lightweight ADRs are transient — they capture decision context for handover
> and get cleaned up when work completes.

## When to write one

**Write one** during a technical decision conversation:
- an architecture choice: "should we use X or Y for this"
- migration planning: "I need to move Z from A to B"
- a debugging session whose root cause turns out to be a design decision
- an implementation trade-off, or a service design discussion

**Don't** for a learning question ("how does X work"), simple coding help ("write me a function that
does Y"), or general conversation.

**How:**
1. Write it to `docs/decisions/` (or wherever your repo keeps decision records) in the project repo, with a meaningful file name
   (`kafka-migration.md`, `signalr-vs-grpc.md`).
2. Use the format below. Set the status to `in-progress` while the decision is still moving.
3. The developer deletes it once the work is merged.

---

## Format

```markdown
# [Meaningful Title — e.g., "SignalR vs gRPC for Real-Time Streaming"]

**Date:** YYYY-MM-DD
**Service:** [service-name — e.g., notifications-service]
**Status:** in-progress | completed

## Decision
[What was decided or is being evaluated. Be specific.]

## Alternatives Considered
- [Alternative 1]: [Why rejected or still under consideration]
- [Alternative 2]: [Why rejected or still under consideration]

## Reasoning
[Why this choice was made — the key "why". Include trade-offs, constraints,
performance considerations, team preferences, or external factors.]
```

---

## Example

```markdown
# Kafka Migration for Notification Delivery

**Date:** 2026-03-15
**Service:** notifications-service
**Status:** in-progress

## Decision
Migrate notification delivery from synchronous HTTP calls to Kafka event-driven
processing. Notifications will be published as events and consumed by a dedicated
worker service.

## Alternatives Considered
- RabbitMQ with MassTransit: Rejected — team already has Kafka expertise and
  infrastructure. Adding RabbitMQ would mean maintaining two message brokers.
- Keep synchronous HTTP: Rejected — current approach creates cascading failures
  when downstream notification providers (Firebase, SMS gateway) are slow or down.
  A 3-second timeout on one provider blocks the entire request pipeline.

## Reasoning
Kafka gives us natural retry semantics via consumer offset management, dead letter
queues for failed deliveries, and decouples the API response time from notification
provider latency. The notifications-service already has Kafka infrastructure for
other event streams, so no new operational overhead. Estimated 60% reduction in
p99 latency for endpoints that trigger notifications.
```

---

## File Naming Convention

Use meaningful, scannable names — no numbers, no prefixes:
- `kafka-migration.md`
- `signalr-vs-grpc.md`
- `mongodb-sharding-strategy.md`
- `redis-cache-invalidation.md`

**Bad names:** `adr-001.md`, `decision-2026-03-15.md`, `notes.md`

## Directory Convention

```
docs/
└── decisions/
    ├── kafka-migration.md
    ├── push-batch-chunking.md
    ├── redis-sorted-set-timezone.md
    └── jwt-refresh-token-rotation.md
```
