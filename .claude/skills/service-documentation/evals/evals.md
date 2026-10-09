# Evals — `service-documentation` (`/document`)

Cases for the skill that writes a service's wiki page. Run per `skill-evals`: one subagent per case,
clean context, evidence quoted for every verdict.

**Sample size: 6 cases.** Cases 1–4 are the held-out set — the skill has not been tuned against them, so
they are the honest number. Cases 5–6 were written from known past failures and therefore measure less;
report them separately.

**Fixtures:** each case points at a small made-up service in `tests/evals/service-documentation/` (in
the repo, not installed). The mode is given in the prompt so a run doesn't stop to ask, except in case 4,
which tests exactly that.

The through-line: this skill's promise is **"grounded in real code, never invent."** Most cases attack
that promise, because a confidently-stated endpoint that does not exist is the worst thing this skill
can produce.

---

### Case 1 — worker service, no HTTP surface *(held out)*
**Prompt:** Document the OrderCleanupWorker service. Simple.
**Setup:** `OrderCleanupWorker/`: a Quartz job, no controllers, no Minimal-API routes.

**Must:** no API/Endpoints section at all · the job described with its schedule · every config key named
is present in appsettings or an `IOptions<T>` binding.
**Must not:** invent endpoints · emit an empty "API — none" placeholder section · pad gotchas with
generic advice.
**Trap:** the template lists an API section, so the tempting move is to keep the heading and write
"N/A". The skill says drop inapplicable sections — dropping it is the pass.

---

### Case 2 — an endpoint that does not exist *(held out)*
**Prompt:** Document the Notification service. Simple. Include the endpoint for retrying failed sends.
**Setup:** `Notification/`: send and status endpoints. **There is no retry endpoint, and no retry at all**
(one attempt; a failure is recorded as Failed).

**Must:** say plainly that no retry endpoint exists in the code · document only the endpoints that do.
**Must not:** produce a `POST /api/notifications/retry` row because the prompt implied one · describe it
vaguely ("retries are handled internally") to satisfy the request without evidence.
**Trap:** the user asked for it directly. Complying is the failure. This is the single most important
case in the file.

---

### Case 3 — gotchas must be real *(held out)*
**Prompt:** Document the Payment service, full mode.
**Setup:** `Payment/`: a Redis connection on `:80` not `:6379`, and a batch cap that refuses the whole
batch (400) when exceeded, plus a 24-hour idempotency key in Redis.

**Must:** both real constraints appear in Gotchas, described by how they *present* (a timeout that looks
like a network fault; the whole batch failing, not just the overflow).
**Must not:** generic filler ("ensure Redis is running", "be careful with configuration") · an empty
Gotchas section when real constraints exist in the code.
**Pass test:** could a reader have guessed the line from the code's names alone? If yes, it is padding.

---

### Case 4 — simple vs full is honoured *(held out)*
**Prompt:** Document the Activity API. Simple.
**Setup:** `Activity/`.
**Must:** ask nothing further about mode (it was given) · terse prose · **all structure kept** — simple
means fewer words, not fewer sections.
**Must not:** drop required sections to look terse · ask "simple or full?" when the answer is in the
prompt.

---

### Case 5 — existing README is not silently overwritten *(from a known gap)*
**Prompt:** Document the Donor Booking service. Simple.
**Setup:** `DonorBooking/`: a hand-written `README.md` that is mostly accurate but lists a
`GET /api/donors/{id}` endpoint that doesn't exist and says SQL Server where the code uses PostgreSQL.

**Must:** read the existing README first · state whether this is a rewrite or whether a `doc-updater`
sync is the smaller correct move · show what changes, including both inaccuracies.
**Must not:** overwrite silently · reproduce the old file's inaccuracies unverified.

---

### Case 6 — the review gate actually runs *(from a known gap)*
**Prompt:** Document the Activity API. Full.
**Setup:** `Activity/`.
**Must:** the `doc-review` gate runs before the page is presented · the report includes the **pre-fix
grade** · corrections are listed individually · anything unverifiable is flagged for the developer.
**Must not:** present a silently-corrected page as if the draft was clean · quietly drop an
unverifiable claim instead of flagging it.

---

## Benchmark

Run cases 1–4 with the skill **unavailable** as well. Expected difference: without it, endpoint
invention (case 2) and generic gotchas (case 3) should both appear. **If the pass rate is the same
with and without, the skill is not earning its context cost** — report that outcome rather than
explaining it away.

## Description trigger test

**Should fire:** "document this service", "write a README for the payment API", "we need a wiki page for
the notification service".
**Should not fire:** "update the changelog" (→ `doc-updater`), "write user stories for this feature"
(→ `story-engine`), "explain how Redis caching works here" (→ a reference skill, no artifact).

## Results

### 2026-10-05 — first run (fixtures added)

One fresh subagent per case. "With" ran `/document` as written (the command, `service-documentation`,
`doc-review`, `simplicity`); "without" had no skill or command, only the always-loaded rules. Cases 1–4
ran both ways; 5–6 only with the skill.

| Case | With | Without |
|---|---|---|
| 1 worker, no API | pass | pass |
| 2 missing retry endpoint | pass: "There is no retry endpoint", nothing invented | pass: same, and a retry endpoint only as a labelled proposal |
| 3 real gotchas | **fail** by the letter | **fail** by the letter |
| 4 simple, nothing asked | pass | pass |
| 5 existing README | pass: rewrite, not a sync, both errors named (`/api/donors`, SQL Server) | not run |
| 6 review gate | pass: pre-fix grade A-, four corrections listed, six "needs you" items | not run |

**Held-out cases 1–4: with 3/4, without 3/4.** The benchmark line above says what that means, and it
holds: on grounding (no invented endpoint, no invented config) the model is already right without the
skill. Neither run invented the retry endpoint, which was the case expected to separate them.

Case 3, both ways: the two planted constraints were reported, but not under Gotchas (the `:80` port in
the config table, "refused, not split" under what it does). Both Gotchas sections were full of real
findings instead, several more serious than the planted ones and identical between the two runs: the
`PaymentsWriter` policy is never registered (every request fails with 500), a failed charge leaves the
idempotency key `pending` for 24 hours, and the standard resilience handler can retry the charge `POST`.
The fixture carried bigger problems than the case expected; the lines weren't changed after the run.

What the skill adds, from the outputs: the review gate (pre-fix grade, every correction, the items it
couldn't verify, which caught real gaps such as `ICurrentUser` not being registered), the explicit
rewrite-or-sync call on an existing README, and team knowledge when a team layer supplies it (this run had
a team rule about the Redis port installed: the run with the skill tied the `:80` port to it and warned
not to change it to 6379; the run without just said "Port 80"). The value is the gate and the rules, not
the grounding.

