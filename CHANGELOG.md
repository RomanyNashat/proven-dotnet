# Changelog

Versions are X.Y.Z: Z a fix or build step, Y a finished feature, X only when updating needs something
from you. Until v1.0.0 nothing is meant to be installed yet.

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
