---
name: doc-review
description: The self-check gate for /document: verifies a generated service doc against real code before presenting — traces every claim (endpoints, deps, config, jobs) to source, finds what was missed, checks the voice, reports a pre-fix grade.
---

# Doc Review — the gate before a service doc is presented

Runs as the **third stage of `/document`** (read → write → **review** → present), the same shape as
`/stories` (generate → review → present). It exists because a wiki page is read by people who will
*trust it*, and the single worst failure is a confidently-stated endpoint or config key that does not
exist.

**Why this gate is trustworthy even though the same model wrote the draft:** it is not judging its own
prose. It is **checking facts against source** — an endpoint either exists in the code or it does not.
That is verification, not self-assessment. So re-read the *code*, not the draft's reasoning. Never
confirm a claim because the draft asserts it; confirm it because you found it.

## Stage A — Groundedness (the important one)

Take every factual claim in the doc and trace it back to real code. Prefer the `proven-roslyn` MCP; fall
back to reading files.

| Claim in the doc | Must be found in |
|---|---|
| An endpoint (method + path) | a route attribute / Minimal-API mapping |
| A dependency (DB, cache, broker, service) | the DI wiring or package reference |
| A config key | `appsettings*.json`, env usage, or an `IOptions<T>` binding |
| A background job / consumer / cron | a hosted service, consumer registration, or job class |
| A "run locally" step | the actual project layout and scripts |
| A stated limit / default / TTL | the code or config that sets it |

**Anything that cannot be traced is not a nitpick — it is a fabrication.** Remove it, or replace it
with what the code actually says, and record the correction. Do not soften it into a vaguer sentence
that is still unverified.

## Stage B — Completeness (what was missed)

The reverse sweep, and where `/document` earns its keep over a hand-written page:
- Endpoints that exist in code but are **absent from the doc** — add them, or state plainly why they're
  excluded (internal/health/deprecated).
- A dependency that is wired but undocumented (a broker, a second store, an external API).
- A background job or consumer nobody wrote down.
- A required config key with no default — the thing that breaks a new deployer.

## Stage C — Voice rules and shape

- **Simplicity layer** — plain language, verb bank, no AI tells. `request/response`, never "emit";
  no vague abstraction where a real action belongs. No padding.
- **No inapplicable sections** — a worker has no API section; a stateless read API has no consumers.
  An empty or filler section is a failure, not a placeholder.
- **Gotchas must be real and substantive.** "Be careful with config" is filler. A real gotcha names the
  actual constraint (the Redis `:80` connection rule, a UTC+3 expiry quirk, a required header). If
  there are genuinely none, say so in one line rather than padding.
- **Descriptions state behaviour**, not a restatement of the route. `Returns the customer's active
  orders` — not `Gets orders`.

## Auto-fix, then report

Fix what you can (remove unverifiable claims, add missed endpoints, correct wrong values, rewrite
off-voice lines), then report. Never present silently-corrected work as if the draft was clean — the
**pre-fix grade is the real signal of generation quality**.

```
Doc review — Notification Service
Pre-fix grade: B

Corrected (4):
  - REMOVED  GET /api/notifications/pending — no such route in code (invented)
  - FIXED    Redis TTL stated 30m; NotificationCacheOptions sets 5m
  - ADDED    POST /api/notifications/bulk — exists, was missing from the doc
  - VOICE    "strengthened the retry path" → "retries failed sends three times"

Needs you (1):
  ? "Requires VPN access" — cannot be verified from the code. Confirm or drop.

Post-fix: A
```

**Three honest outcomes per claim** — the same discipline as the parity gate:
- ✅ **verified** — traced to source.
- ⚠️ **corrected** — was wrong, now right, and the correction is listed.
- ❌ **can't verify** — cannot be traced *and* cannot be safely removed (an operational fact like VPN
  access or a team convention). **Flag it for the developer — never quietly keep it and never quietly
  delete it.** This third bucket is the point: the doc must not carry unverified claims silently.

## Rules
- Re-read the **code**, not the draft's reasoning. A claim is confirmed by finding it, not by it being
  written confidently.
- An untraceable claim is a fabrication — remove or correct it, don't soften it.
- Run the reverse sweep too; a doc that is accurate but incomplete still misleads.
- Auto-fix, then report the **pre-fix grade** and every correction. Never present as clean.
- Anything unverifiable that can't be dropped goes to the developer, explicitly.
