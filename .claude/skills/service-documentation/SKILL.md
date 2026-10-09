---
name: service-documentation
description: /document mode: write wiki/README docs for a .NET service, grounded in real code — endpoints, deps, config, jobs, gotchas. Uses the simplicity layer.
---

# Service Documentation — document a service for the wiki

Writes the documentation page a teammate reads to understand a service — grounded in the **real code**,
not a generic template. It reads what the service actually is (its endpoints, dependencies, config,
background work) and writes it up plainly. Powers `/document`.

**Uses the simplicity layer** — this is shareable writing, so ask **simple or full** first (per the
`simplicity` skill), and follow the plain-writing rules (verb bank, no AI tells, name the real thing).
The output goes on a wiki; it should read like a senior engineer wrote it, not like generated filler.

## Scope — this writes a NEW page

- **`/document` (this skill) = author the page**, reading the whole service to build it from scratch,
  or rewrite one that has drifted badly.
- **Keeping an existing doc in sync after a change = the `doc-updater` agent.** A config key changed, a
  changelog row is needed, an endpoint description is stale — that's a sync, not an authoring job.
- If a README already exists, **read it first** and say plainly whether you're rewriting it or whether
  a `doc-updater` sync is the smaller correct move. Never silently overwrite a page someone wrote by
  hand — show the developer what changes.

## Ground it in the code (read first, write second)
Before writing, read the service to learn what's actually true. Prefer the `proven-roslyn` MCP for the
code facts (endpoints, types, references); fall back to reading files.
- **What it is / does** — the service's purpose, from its name, its endpoints, and its README if one
  exists. One or two plain sentences.
- **API surface** — the endpoints/controllers or Minimal-API routes: method, path, what each does.
  Group by area. (Roslyn/route attributes are the source of truth — don't invent endpoints.)
- **Dependencies** — what it talks to: databases (EF/Dapper/what store), caches (Redis), brokers
  (Kafka/RabbitMQ/NATS), other services, external APIs. Read these from the wiring, not guessed.
- **Configuration** — the settings that matter: connection strings, feature flags, the
  `appsettings`/env keys a deployer must set. Note which are required.
- **Background work** — hosted services, consumers, cron/console jobs, scheduled work.
- **How to run it locally** — restore/build/run, required local dependencies, the connection details
  (e.g. a non-default port the platform requires).
- **Gotchas** — the real ones: platform quirks, non-obvious config, known constraints. This is the
  most valuable section and the one a generic template misses — pull it from comments, README notes,
  and anything surprising in the wiring.

## Structure of the page (adapt to the service)
A sensible default, trimmed to what the service actually has:
1. **Overview** — what it is and why it exists (2–4 sentences).
2. **What it does** — the main capabilities, plainly.
3. **API / endpoints** — grouped, with method + path + one line each (skip if it's a worker with no API).
4. **Dependencies** — the stores/brokers/services it uses.
5. **Configuration** — required + notable settings.
6. **Running locally** — the concrete steps.
7. **Background jobs / consumers** — if any.
8. **Gotchas / notes** — the non-obvious stuff.

Drop sections that don't apply (a background worker has no "API"; a stateless read API has no
"consumers"). Don't pad — a shorter true page beats a long templated one.

## The template — fill this in

A rule alone doesn't land; copy this shape. Drop any section the service doesn't have.

```markdown
# <Service Name>

<One or two plain sentences: what it is and why it exists. Name the real job it does,
not "a service that handles X".>

## What it does
- <capability, stated as an action the service performs>
- <capability>

## API
| Method | Path | What it does |
|---|---|---|
| GET | /api/... | Returns ... |
| POST | /api/... | Creates ... |

## Depends on
- **<SQL Server / Postgres>** — <what it stores>
- **Redis** — <what it caches>
- **<broker>** — <what it publishes/consumes>
- **<other service>** — <what it calls it for>

## Configuration
| Key | Required | Notes |
|---|---|---|
| `ConnectionStrings:Db` | yes | — |
| `Feature:X` | no | defaults to false |

## Background work
- **<HostedService/consumer/cron>** — <what it does, how often>

## Running locally
```bash
<the actual commands>
```
<any local dependency: a container, a connection detail, a required secret>

## Gotchas
- <the real, non-obvious constraint>
```

## Worked example — the difference the voice makes

The same endpoint, written badly and written well. This is the gap the rules alone don't close.

**✗ Padded and vague — reads like filler:**
```markdown
## API
The service exposes a comprehensive set of endpoints for notification management.

| Method | Path | What it does |
|---|---|---|
| GET | /api/notifications | Gets notifications |
| POST | /api/notifications/send | Sends a notification |

## Gotchas
- Be careful with the configuration.
- Make sure Redis is running.
```
Problems: "comprehensive set" is padding; "Gets notifications" restates the route and says nothing
about *which* ones; the gotchas are generic enough to apply to any service, which means they're worth
nothing.

**✓ Real and specific:**
```markdown
## API
| Method | Path | What it does |
|---|---|---|
| GET | /api/notifications | Returns the caller's unread notifications, newest first, max 50 |
| POST | /api/notifications/send | Queues a push for one user; returns 202 with the job id |

## Gotchas
- Redis is reached as `redis-service:80`, not `:6379` — the platform maps it. Using 6379 fails with a
  timeout that looks like a network problem.
- `Notification:BatchSize` above 500 trips FCM's per-request cap and the whole batch fails, not just
  the overflow.
```
Each line says something a reader could not have guessed from the route name, and each gotcha names a
real failure and how it presents.

**The test for any line you write:** could a reader have guessed it from the code's names alone? If yes,
it's padding. If it would have cost them an hour to discover, it belongs.

## Simple vs full
- **Simple** (default lean) — the sections above, terse, the essentials a teammate needs to be
  productive. Good for most service READMEs.
- **Full** — adds more detail: per-endpoint request/response shapes, sequence of a key flow, deeper
  config tables, architecture notes. Ask which before writing; don't assume full.
Either way: plain language, real names, no AI tells (`simplicity` rules apply throughout).

## Output
Markdown, ready to paste into the wiki (or committed as the service `README.md`). Plan-first: present
the doc; the developer places it. Never invent facts — if something can't be read from the code, say
so or ask, rather than filling it in.

## The review gate (always runs)
After writing, the `doc-review` skill verifies the page before the developer sees it: it traces every
claim back to source, sweeps for what was missed, checks the voice, auto-fixes, and reports a
pre-fix grade plus anything it could not verify. Writing is not done until the gate has run.

## Evals
Test cases for this skill live in **`evals/evals.md`** beside it (a Level-3 file — it costs nothing
against the skill-listing budget). Run them per the `skill-evals` skill before and after changing this
skill. Cases 1–4 are held out; lead with those numbers.

## Rules
- Ground every claim in the real code — never invent endpoints, dependencies, or config.
- Ask simple or full first; apply the simplicity plain-writing rules throughout.
- Adapt the structure to what the service is; drop inapplicable sections; don't pad.
- The gotchas section is the point — capture the non-obvious, real constraints.
- Present the doc for the developer to place; don't assume where it goes.
