# Changelog

Versions are X.Y.Z: Z a fix or build step, Y a finished feature, X only when updating needs something
from you. Until v1.0.0 nothing is meant to be installed yet.

## 0.1.12 — dotnet-core tested
- `dotnet-core` 2.0.0, its code tested in CI, with story and production tests. It now covers what its
  description promised (the host, DI lifetimes, options) and wasn't there. What the tests found:
  - **In Production the host doesn't check service lifetimes.** A singleton that takes a scoped service
    builds fine and gives two requests the same "scoped" instance (for a DbContext: two users on one
    context). The skill turns `ValidateScopes` and `ValidateOnBuild` on everywhere; `ValidateOnBuild`
    alone doesn't catch it.
  - **`required` doesn't protect options:** the binder leaves a missing setting null. `[Required]` with
    `ValidateOnStart()` stops the deploy instead of the first request; custom checks must be null-safe,
    or their `NullReferenceException` hides the real message.
  - **A base address without its trailing `/`, or a call path with a leading one, drops the path**
    (`/api/` disappears). Checked at start-up now.
  - The typed-client sample didn't compile (`EnsureSuccessStatusCode` on a `Task`), the named client had
    a URL in code, the `field` sample cached a slug that went stale, the extension-block sample put a
    constraint where C# doesn't allow one, and the anti-patterns code block was never closed.

## 0.1.11 — story and production tests
- A new bar for tested skills: besides the code samples, each one gets **story tests** (a real situation,
  Given / When / Then) and **production tests** (the same code under a slim image: no ICU, no tzdata,
  UTC). CI runs the production tests in their own step under those conditions, and every one checks
  them first (`ProductionConditions.Require()`). `tests/story-and-production.txt` lists the skills that
  must keep both; `check-skill-samples.py` fails if one loses them and lists the 24 still to do.
- Done for `hangfire-patterns` and `quartz-scheduling`. What they found:
  - **Both samples crashed on an image without tzdata:** `FindSystemTimeZoneById("Asia/Riyadh")` throws
    there. They now use `RiyadhTime` (`localization` §7), which falls back to a fixed UTC+3 zone; the
    production tests get 23:00 UTC with no tzdata.
  - **Hangfire lost the zone anyway:** it stores the zone's id and looks it up again by id, which throws
    without tzdata. The setup now registers an `ITimeZoneResolver`, which Hangfire's scheduler, dashboard
    and `AddOrUpdate` all take from DI.
  - **Quartz dropped a run missed while the service was down.** Every start replaces the stored trigger
    and, by default, works the next run out from now. `ScheduleTriggerRelativeToReplacedTrigger = true`
    keeps it; the story runs the missed report once, for the slot it missed, and a second story shows
    the default dropping it.
  - Hangfire: a receipt queued while no worker runs goes out once when a worker starts.
- `tools/ci/refresh-samples.py` rewrites a skill's sample blocks from the tested files.

## 0.1.10 — quartz-scheduling tested
- `quartz-scheduling` 2.0.0, its code tested in CI against PostgreSQL. What the tests and the rewrite found:
  - Quartz's own schema script **drops every Quartz table** by default (`DropDb := 1`). The skill now
    says to set it to 0 and apply it through the pipeline; its `text`/`bytea` columns are a recorded
    exception to the column rules.
  - `UseMicrosoftDependencyInjectionJobFactory()` is obsolete (and breaks a warnings-as-errors build).
  - Cron ran in the pod's time zone (UTC) while the comment said "2 AM"; now `InTimeZone(Asia/Riyadh)`,
    tested as 23:00 UTC, with an explicit misfire rule.
  - `0 */30 8-17` was described as "business hours 8-17"; its last run is 17:30.
  - Admin endpoints paused and resumed jobs that didn't exist without a word; now 404.
  - The health check is unhealthy for a moment at start-up: readiness, not liveness.
- Also tested: no overlapping runs, a restart keeps one stored job, and Quartz's static logger in tests.

## 0.1.9 — hangfire-patterns tested
- `hangfire-patterns` 2.0.0, its code tested in CI against PostgreSQL. What the tests found in the old text:
  - `[AutomaticRetry]` on the job class is ignored when the job is enqueued through its interface:
    the job kept retrying. Filters go on the interface method.
  - The schema was created by the app at startup (`PrepareSchemaIfNecessary = true`), against the
    migrations rule. It's now a pipeline step, and Hangfire's unbounded columns are a recorded exception.
  - A Windows time-zone id that fails on Alpine without ICU; now `Asia/Riyadh`, and 02:00 there is
    tested as 23:00 UTC.
  - Guid job arguments (now `int`), an "idempotent" job that would send twice after a crash, and a
    dashboard filter that left the default local-only filter in place.
  - Queue order is a preference, not a reservation: urgent work gets its own server.
- Also tested: fire-and-forget gets Hangfire's cancellable token, continuations never run after a failed
  parent, and the dashboard returns 401/403/200 by policy.

## 0.1.8 — per-project skills
- `project-skills/`: Cassandra, FHIR, BigQuery, Firebase, SignalR, NATS, RabbitMQ, CAP and GitHub
  Actions. Not installed; copy one into the repo that uses it, so niche skills don't crowd the global
  list. Their code isn't tested in CI yet, and the README says so.

## 0.1.7 — production diagnostics
- `production-diagnostics`: turn "slow, leaking, restarting, starving" into evidence from logs and APM,
  with a symptom playbook, the container GC facts behind restarts, and safe requests to whoever runs the
  cluster (no memory dumps by default). Its runtime heartbeat, one log line a minute with the runtime's
  own counters, is tested in CI.
- Every skill and command the content mentions now exists, and the content check fails if one goes
  missing.

## 0.1.6 — handover, stories, lessons, scaffolding and service docs
- `/handover`: a vacation handover across all your repos. One read-only analyst per repo reads the
  journal, surviving sessions, checkpoints, ADRs and git in parallel; every item says where it came
  from, and anything git alone shows becomes a question for you. Writes Markdown, Teams and Notion.
  A team layer can add a `handover-<team>` skill with its own pre-fills.
- `/stories`: epics, stories and sub-tasks from requirements or from existing code, with a reviewer
  that grades the draft and fixes it before you see it. Exports Markdown and a Jira CSV.
- `/lessons` and `/improve`: capture what a session taught, fold it in one approved change at a time,
  and audit the harness against its own rules.
- `/scaffold` (including from existing SQL) and `/document` (a service page grounded in the code, with
  its own review gate).
- Settings keep session history for 120 days, so `/handover` can still read it after a long stretch.
- CI checks that reviewers and analysts stay read-only, and tests the handover helper scripts.

## 0.1.5 — agents and commands
- 15 agents: planner, architect, tdd-guide, the three read-only reviewers (code, security, DBA),
  e2e-runner, build-error-resolver, refactor-cleaner, doc-updater, devops-engineer, coverage-analyst,
  health-analyst, pattern-analyst and the opt-in compliance-auditor. All run on Opus.
- 20 commands, among them `/plan-feature`, `/tdd`, `/full-review`, `/quality-gate`, `/coverage`,
  `/health-check`, `/checkpoint`, `/sleep`, `/wake-up` and `/version` (which now reads the installer's
  registry, so it shows every layer installed).
- 21 more skills behind them: the plain-writing layer, the parity gate for refactors, coverage and
  health grading, TDD, zero known vulnerabilities, caching, scheduling (Quartz, Hangfire, CronJobs),
  Docker, Kubernetes, Aspire, Azure, k6, OpenIddict and others.
- The settings start worktrees from the current commit (`worktree.baseRef: head`), which the parity
  gate relies on.

## 0.1.4 — the Roslyn server
- `proven-roslyn-mcp`, an MCP server that answers symbol questions (who calls this, where is it
  defined, what implements it, is it dead) from Roslyn instead of text search, plus a safe rename.
  Install it with `install-roslyn.sh` / `install-roslyn.ps1`; it registers as `proven-roslyn`.
- Built and packed on Linux and Windows in CI, with regression tests for the load failures seen in real
  use (a warm-up that waited on itself, `.slnx` files, several solutions under one folder).
- The layer's settings raise the MCP connect timeout so a large solution can finish loading.

## 0.1.3 — the hooks
- Python hooks, installed in `~/.claude/hooks/proven/` and wired into `settings.json` by the installer:
  session start and checkpoint, the work journal (raw capture with personal data and secrets masked),
  formatting after edits, pre-compact state, the end-of-turn hook, the subagent log, and a guard that
  stops a Roslyn rename from editing the main checkout while working in a worktree.
- The installer starts hooks with the right Python for the machine (`python3` on macOS and Linux,
  `python` or the `py` launcher on Windows), so they don't fail where `python` doesn't exist.
- Settings this layer adds: its hook profile, the task list switch, and a deny list for destructive
  shell commands. Nothing else of yours is changed.
- Checked with a real Claude Code session: the hooks fire from the installed paths.

## 0.1.2 — the tested skills
- 23 skills whose code runs in CI against real dependencies: API design, auth, CQRS, Dapper, DDD,
  EF Core, encryption, gRPC, Kafka, localization, MongoDB, nginx, outbox, OWASP, PII masking, Polly,
  PostgreSQL, Redis, secret management, SQL Server, integration testing, workers, design patterns.
- `tests/SkillSamples.Tests`: the code the skills show, run against PostgreSQL 17, SQL Server 2022,
  MongoDB 7 (replica set), Redis 7, Kafka 3.9 and nginx, in two parallel CI jobs. A skill marks each
  tested block with `<!-- sample: path -->`, and CI fails if the block and the file drift apart.
- Fixed a flaky test setup found on the first public run: test classes run in parallel (the runner
  config that said otherwise was never copied to the output), and the MongoDB fixture dropped every
  test database when its class finished, including another class's database mid-test. Each fixture now
  drops only its own; the misleading config is gone.

## 0.1.1 — the rules
- The always-on instructions (`core.md`) and the ten rule files: architecture, ASP.NET Core, C#,
  coding style, EF Core, git, performance, security, testing, and agent routing. Where teams
  reasonably differ (mocking library, branch model, key type), the rules give a default and say "unless
  your team's rules say otherwise".
- The lightweight ADR template.
- A content check in CI: `core.md` under 200 lines, ASCII-only PowerShell, and a list of the skills and
  commands the rules mention that haven't moved in yet.

## 0.1.0 — the installer
- `tools/layer.py` installs, updates and uninstalls a layer in `~/.claude` without touching the user's
  own files or settings. Tested on Linux and Windows.
