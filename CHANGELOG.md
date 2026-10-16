# Changelog

Versions are X.Y.Z: Z a fix or build step, Y a finished feature, X only when updating needs something
from you. Until v1.0.0 nothing is meant to be installed yet.

## 0.2.17 — does the fix work?
- `tests/evals/fix-hard/`: an eval that grades fixes, not findings. A run reviews and fixes a service with
  14 planted findings; hidden tests decide, by behavior, whether each is fixed and whether five things that
  worked still work. No judge. `fix-evals.yml` checks the eval itself on every change (the original fails
  every finding test, the reference fix passes them all) and grades every copy under `runs/`. H03 runs with
  no time-zone database, H04 on an ICU whose `ar-SA` is Hijri, H11 is a NuGet audit.
- First runs in `results.md`: with proven 13/14 findings fixed, rules only 12, no rules 10; every run built
  and kept all five regressions. All three missed the same one (H12, below).
- `pii-masking` 1.5.0: `HttpClient` logs a failed request's exception, message and all, so a provider
  error that repeats the number lands in the logs whatever the service's own catch does.
  `.RemoveAllLoggers()` on that client stops it. Tested; found by the reference fix in CI.
- `pii-masking`: what `UseSerilog()` beside `EnableRedaction()` really does, tested in a real host. With
  `EnableRedaction()` after it, nothing logged through `ILogger` reaches Serilog; before it, the
  classified field vanishes. The setup example now adds Serilog as a provider.
- `known-traps` 1.2.0 (31 checks): `serilog-factory-with-redaction`; a start-up check that `ar-SA` exists
  is no longer reported as formatting with its default calendar.

## 0.2.16 — the skills' remaining code, tested
Every skill that shows code now has it tested, with a story test and a production test. What the tests
found was fixed in the skill:
- `cronjob-patterns` 1.1.0: "yesterday" is computed in the clinic's time zone (UTC gives the wrong day
  after midnight); a backfill pages by id, because paging by "still to do" never ends; the outcome is read
  before `RunAsync` disposes the host; a console job reads `DOTNET_ENVIRONMENT`, not
  `ASPNETCORE_ENVIRONMENT`. The job's Dockerfile builds again (a missing stage, a bad restore).
- `kubernetes-dotnet` 2.0.0: readiness fails and liveness stays up when Redis is down; on .NET 10 a
  stop right after start cuts a worker before it runs; `ShutdownTimeout` is 30 s by default; `preStop`
  uses `sleep` (chiseled images have no `sleep` binary); `DOTNET_EnableDiagnostics=0` also blocks
  dotnet-counters, so turn off only the debugger and profiler. Manifests checked with kubeconform.
- `caching` 2.0.0: HybridCache on real Redis: one call under a stampede, tags, and the stale L1 on other
  pods, closed by a pub/sub eviction.
- `aspire-patterns` 2.0.0: an AppHost and its integration test run in CI. The standard resilience handler
  retries POSTs; `DisableForUnsafeHttpMethods` sends them once. Health endpoints mapped in Production,
  probes kept out of traces. A service with no launch profile has no endpoints in the AppHost; Aspire
  13.0.0 needs a MessagePack pin, and StackExchange.Redis 2.9.32 or later, to restore with NuGetAudit as errors.
- `code-quality` 1.1.0: the props and `.editorconfig` built with the CLI. MA0004 must be off beside
  CA2007, and IDE1006 needs its own severity line to fail a command-line build.
- `docker-dotnet` 1.1.0: four stages, built for real; a curl health check on chiseled stays unhealthy;
  chiseled is invariant and chiseled-extra has ICU; Kafka in compose needs a base64 `CLUSTER_ID`; a
  `dotnet test || true` stage hides failures.
- `azure-deployment` 1.1.0: a token-refreshing Npgsql data source recovers after a refused token;
  `UseAzureSqlDefaults` is obsolete. The Bicep compiles.
- `k6-load-testing` 1.1.0: the script runs; k6 exits 99 when a threshold is crossed.
- `localization`: a Windows time-zone id works only with ICU; `known-traps` 1.1.0 checks for it again
  (30 checks), and its SignalR check sees a group name built from a parameter.
- Ingress: `rate-limit` and `rate-limit-window` aren't ingress-nginx annotations; `limit-rpm` is.

New:
- `api-contract-evolution` 1.0.0: what breaks an app already installed on phones (a new enum value, a new
  required field, a renamed property), versioning and deprecation headers, and oasdiff set so a new
  response enum value fails the build (by default it is only a warning). `api-design` 3.2.0 links to it.
- `try-first.ps1`: install into a throwaway folder, check what landed, and remove it. Tested on Windows
  PowerShell 5.1 and PowerShell 7. The installers honour `PROVEN_CLAUDE_DIR`.
- `tests/evals/skills-only/`: a fixture whose findings the rules don't mention, run three ways (proven
  10/10, rules only 9.5, no rules 9.5).
- CI: `infra-samples.yml` checks the Dockerfiles, compose, manifests, Bicep and k6, and runs the Aspire
  AppHost.

## 0.2.15 — what a reference token costs per request
- `auth-patterns` 2.1.1: OpenIddict.Validation doesn't cache introspection, now tested: ten requests with
  one token are ten introspections, each two queries on the auth server's database. What to read in the
  APM before deciding on a cache, which delays a revocation by its lifetime.
- `tests/evals/full-review-hard/results.md`: a baseline with no rules at all (10/14, against 10.5 with the
  rules and 11.5 with proven), and the known-traps scan on the same fixture (9/14 with no model, including
  the two findings every review missed most).

## 0.2.14 — one scan for the known traps
- New `known-traps` skill: `scan_traps.py` scans one service or many for 29 traps the skills' own tests
  found (a Quartz store that drops missed runs, SqlClient in invariant mode, a singleton holding a
  `DbContext`, `ar-SA`'s machine-dependent calendar, OpenIddict 6 on EF Core 10, an open CORS policy and
  more). Read-only, standard library only, a table with file, line, fix and the skill that proves it;
  `--json` and `--fail-on` for pipelines. Tested on Ubuntu and Windows: every check fires on a service
  that has its trap and stays quiet on the corrected copy.
- `/full-review` runs the scan first and hands its table to the three reviewers.

## 0.2.13 — an eval for production-diagnostics
- `tests/evals/production-diagnostics/`: three incidents as a developer gets them (heartbeat CSV, request
  logs or APM numbers, what SRE sent, the suspect code): thread-pool starvation from `.Result`, a pod
  throttled at a half-core limit with Server GC, and a leak from an unbounded static cache ending in
  OOM kills. `evals.md` holds the answers, what each run must find and what it must not do (blame a
  victim, ask for a full dump of patient data, raise the limit as the fix).
- First runs in `results.md`: with proven 9/9 Musts, rules-only baseline 8.5/9; all three diagnoses
  right and no Must-not broken either way, graded blind. The baseline's slips were in the memory case
  (a counter misread, a gcdump asked for without its cost).
- `testing-tdd` 2.3.1: why there's no generated wrapper for statics. .NET has the seams; where it
  doesn't, a small interface for what the code does beats a mirror of `File`.

## 0.2.12 — the test gap in a change
- `test-coverage` 1.2.0: `scripts/changed_uncovered.py` takes a diff and a Cobertura report and lists the
  changed lines no test runs, by method, plus changed files no test loads; `--min` makes it a gate.
  Reads a diff saved by Windows PowerShell (UTF-16). Tested in CI, including the story of a merge request
  that adds a test and still leaves half its changed lines untested.

## 0.2.11 — [LogProperties], and FakeLogger in our own tests
- `pii-masking` 1.4.0: `[LogProperties]` logs every property of an object; only the classified ones are
  redacted. Tested: a property added later without the attribute goes out in clear.
- The OpenIddict and CAP tests collect the server's errors with `FakeLogCollector` instead of a
  hand-written logger provider.

## 0.2.10 — the harder fixture, run
- `tests/evals/full-review-hard/results.md`: with proven's three reviewers 11.5/14, rules-only baseline
  10.5/14, 0 distractors flagged by either, graded blind by a separate agent. The one difference was the
  transitive Pkcs vulnerability. Both missed the Quartz misfire setting and the unmasked exception text.
- `dba-reviewer`: a scheduler checklist (misfire setting, `InTimeZone`, clustering and its tables).
- `security-reviewer`: exception text in log calls isn't masked; Serilog must be a provider under
  `EnableRedaction`.

## 0.2.9 — a harder /full-review fixture
- `tests/evals/full-review-hard/ClinicReminders`: 14 planted findings a careful review without the
  skills tends to miss, each one proven by a skill's test (a singleton holding a DbContext, SqlClient on
  an invariant-mode image, Asia/Riyadh without tzdata, `ar-SA`'s machine-dependent calendar, a Quartz
  cron in UTC and dropped misfires, a base address without `/`, `UseSerilog()` bypassing redaction, keyset
  paging on a shared timestamp, a transitive Pkcs vulnerability, and more), with 5 distractors. The
  first fixture was too easy: the baseline found everything too.
- `skill-evals`: a fixture both runs pass measures nothing; how to build one that doesn't.

## 0.2.8 — which calendar a culture writes with
- `localization`: `CultureInfo.Calendar` is the default calendar and doesn't follow the one you set;
  `DateTimeFormat.Calendar` is the one used to write dates. Tested on Ubuntu 26.04, where the two differ
  for the Hijri culture.

## 0.2.7 — a package's API without decompiling it
- `proven-roslyn`: `get_package_api` lists a referenced package's public and protected types and member
  signatures, with their XML summaries, from the metadata the compiler already loaded. Nothing is
  decompiled. It says so when the package isn't referenced instead of guessing. Tested on the sample
  solution: `System.Text.Json`'s `JsonSerializer` and its generic `Serialize`/`Deserialize`, an
  unreferenced package, and internal types left out.
- Every `proven-roslyn` answer now writes `<`, `>` and `&` as they are: the default JSON escaping turned
  each generic signature into `\u003C...\u003E` (found by the new test).
- `core.md`: "how do I call this package" goes to `get_package_api`, after the codebase's own usage and
  before the docs. The agents that use `proven-roslyn` have the tool.

## 0.2.6 — our own tests on Central Package Management
- `tests/Directory.Packages.props` holds one version for each package of the three test projects; the
  projects name packages without versions. The Pkcs security pin is one `PackageVersion` under
  transitive pinning instead of a direct reference in two projects. The samples keep floating
  (`CentralPackageFloatingVersionsEnabled`) so CI keeps testing what a service restores today.
- The eval fixtures under `tests/evals/` opt out: they stand for someone else's service.
- `msbuild` 1.2.0: when floating under CPM is reasonable (a test suite like this one) and when it isn't.

## 0.2.5 — CI on Ubuntu 26.04, and ar-SA's calendar isn't fixed
- Every Linux job runs on `ubuntu-26.04`, pinned rather than `ubuntu-latest`, which moves between
  19 October and 19 November. The Windows jobs are unchanged. `.github/actionlint.yaml` declares the
  new label until the pinned actionlint knows it.
- **Found by the move:** `ar-SA`'s default calendar comes from the machine's ICU. Umm al-Qura (Hijri)
  with Ubuntu 24.04's, Gregorian with 26.04's: three localization tests that relied on the default
  failed. The same code can write a Hijri date on one machine and a Gregorian one on the next.
  `Cultures.ArabicHijri` now sets `UmAlQuraCalendar` itself, the trap test checks the named cultures
  instead of the default, and `localization`, `rules/csharp-standards.md` and `code-reviewer` say so.

## 0.2.4 — three more symptoms
- `production-diagnostics`: playbook rows for CPU at the limit (throttling, confirmed from the
  container's `cpu.stat`), slower since a deploy, and a slow first minute after a start; the safe
  request for `cpu.stat` in §6.

## 0.2.3 — statics a test can't control
- `testing-tdd` 2.3.0: "Code a test can't control": finding the clock, disk, network, random and
  environment statics with `proven-roslyn`'s `find_references`, and the seam for each (`TimeProvider`,
  injected `Random`, `IOptions<T>`, a small file interface, typed `HttpClient`). Moving one is a refactor,
  so a characterization test comes first.

## 0.2.2 — classified logging
- `pii-masking` 1.3.0: a third layer. A `[LoggerMessage]` parameter marked `[PatientIdentifier]` is
  redacted in the logging pipeline before any provider sees it (`Microsoft.Extensions.Compliance.Redaction`
  with `EnableRedaction`). Tested with `FakeLogger` and Serilog: the classified value is `****` in the
  message and the field; an ordinary call with the same value isn't redacted, so the pattern operators
  stay. **Story:** Serilog as a provider keeps redaction; `UseSerilog()` replaces the factory, the
  redactor never runs, and the classified field silently disappears from the line.

## 0.2.1 — the heartbeat measures against the container's limits
- `production-diagnostics`: CPU and memory in the heartbeat line are measured against the container's
  cgroup limits (`ContainerLimits`: v2 with the smallest of the pod's and the container's, v1, or none).
  Before, CPU was a share of `Environment.ProcessorCount`, which rounds half a core up to 1: a pod using
  all of its `500m` read as 50%, and triage blamed thread starvation instead of CPU (tested as a story).
  And `memory limit` was the GC heap limit, 75% of the container's by default; the line now logs both.
- Tested with fake cgroup folders (v1, v2, nested limits, no limits) and on the CI runner's real files
  under production conditions.

## 0.2.0 — release candidate
Everything ECC had, rebuilt in the open and tested: 55 skills, 9 per-project skills, 20 agents and 26
commands. Nothing new in this version; it marks the end of the move.
- **Every skill with code is tested in CI against real dependencies** (PostgreSQL, SQL Server, MongoDB,
  Redis, Kafka, RabbitMQ, NATS, nginx, the dotnet CLI against nuget.org). A skill's code block and the
  tested file can't drift apart.
- **All 39 of them also have story tests** (a real situation as Given / When / Then) **and production
  tests** (the same code without ICU or tzdata, in UTC, as on a slim image). Bugs they found along the
  way are in the 0.1.x notes below: SqlClient refuses to connect without ICU, OpenIddict 6 logout throws
  on EF Core 10, Quartz drops runs missed during downtime, paging on a shared timestamp loses rows, a
  manual "staging" deploy reached production.
- New since ECC: `dotnet-upgrade`, `msbuild`, heartbeat triage in `production-diagnostics`, risk
  ranking in `test-coverage`, and evals for `/full-review` and `/health-check`.

## 0.1.36 — what the deploy workflow decides
- `cicd-github-actions` 1.2.0, the last skill with code to get story and production tests:
  - **Story:** a manual deploy that chose staging, run from `main`, went to production too; the
    production job now runs only for a manual run that chose it.
  - The deployed tag is the commit, not `main` (the same string on every push, so nothing rolled out).
  - Permissions per job instead of for the whole workflow.
  - The PR pipeline runs production-condition tests in their own step (no ICU, no tzdata, UTC). The
    production test checks that step against the conditions it runs under itself.
  - The workflows are read with YamlDotNet; job conditions go through a small evaluator for GitHub's
    expression syntax.
- Every skill with tested code now has both kinds of test.

## 0.1.35 — paging that doesn't lose rows
- `efcore-patterns` 2.4.0: the page order must be unique. Tested as a story on PostgreSQL and SQL Server:
  five visits saved in one instant, paged two at a time by timestamp alone, show two and lose three with
  no error; ending the order with the id, and carrying it in the cursor, returns all five. Also: what
  `AsSplitQuery()` costs (round trips, no shared snapshot, unique order with `Skip`/`Take`), and when
  `AsNoTrackingWithIdentityResolution()` is worth it.
- `dba-reviewer`: two checklist lines for the above.

## 0.1.34 — build outputs that can't clash
- `msbuild` 1.1.0: outputs through `UseArtifactsOutput` with an explicit `ArtifactsPath`. Tested: each
  project gets its own `artifacts/bin` and `artifacts/obj` folder; one `obj` set in
  `Directory.Build.props` puts every project in the same folder (one `project.assets.json` for all);
  and the default artifacts path follows the nearest `Directory.Build.props`, so a nested one moves the
  test projects to `tests/artifacts`.

## 0.1.33 — MCP servers: what read-only means
- Rules: a repo's `.mcp.json` is reviewed like its hooks, and a read-only MCP server isn't a boundary
  while its CLI is allowed: deny the CLI's write commands too. A read-only docs server, when connected,
  is where package questions go before asking the developer.
- README: Microsoft Learn's MCP server as a recommended docs source, and a deny example for CLIs.

## 0.1.32 — MSBuild and central package versions
- `msbuild` (new): `Directory.Build.props` for shared settings, Central Package Management for one
  version per package, moving a solution onto it, and finding out why the build or restore did what it
  did (`-getProperty`, `-pp`, `dotnet nuget why`, binary logs replayed to text, and why a binary log is
  kept private). Tested by restoring a sample solution with the real CLI: a transitive package with known
  vulnerabilities fails the audit until one central line pins it, a project can't take its own version
  (NU1008) or override the central one (NU1013), a range restores its lowest version, and a nested
  `Directory.Build.props` without the import silently drops the shared settings.
- `zero-vulns`: under Central Package Management a transitive pin is a `PackageVersion` with transitive
  pinning, not a direct reference in each project.
- `rules/testing.md`: the FluentAssertions range restores 7.0.0; raising the lower bound is how a 7.x fix
  comes in.
- `build-error-resolver`: package version questions and build-logic questions go through `msbuild`.

## 0.1.31 — test quality: risk ranking and test review
- `test-coverage`: `scripts/coverage_risk.py` ranks methods from a Cobertura report by CRAP score
  (complexity² × (1 − coverage)³ + complexity), folds async state machines back into their methods,
  leaves constructors out, and marks anything above 30 "test before changing". Tested in CI against a
  fixture report (a new "Skill scripts" step).
- `testing-tdd`: "Reviewing a test", a table of what makes a test unable to fail and what to do instead.
  Tested: a setup copied from the happy path onto a path that returns early is found by `VerifyAll()`.
  `code-reviewer` now reviews the tests in a change against it.

## 0.1.30 — testing what gets logged
- `testing-tdd`: a log line's required fields and forbidden content are tested with `FakeLogger<T>`
  (`Microsoft.Extensions.Diagnostics.Testing`), not a Moq setup on `ILogger.Log`. Tested as a story: the
  careful handler's line carries the booking id and nothing about the patient; the test catches the one
  that logs `{@Command}`.

## 0.1.29 — triage from heartbeat lines
- `production-diagnostics`: a window of heartbeat lines becomes **signals** (what is out of range, with
  the numbers) and **hypotheses** (likely cause, why, next check), most likely first. Rules for the
  queue, threads, CPU, memory near the limit and growing, GC pauses, lock contention and exception
  bursts, with thresholds to tune per service. Tested as a story (an API slow at nine with low CPU points
  at blocked threads, not the database), plus: busy cores are CPU-bound, not starving; memory rising
  through full collections is pressure; one bad line raises nothing; a slim-image run.

## 0.1.28 — the tracking rule, stated precisely
- "`AsNoTracking()` on every read" becomes "on a read that returns entities". Tested on both engines: a
  projection to plain values tracks nothing already, so `AsNoTracking()` there changes nothing and
  `dba-reviewer` no longer reports it; an entity inside a projection (`new { Visit = v }`) is tracked.
  `efcore-patterns`, `dba-reviewer` and `rules/performance.md` say the same.

## 0.1.27 — a skill for moving to .NET 10
- `dotnet-upgrade`: moving a service from .NET 8 or 9 to 10 before both leave support on 10 November
  2026, and turning on nullable in an existing codebase. The order of work, what breaks in a backend
  service, and how to prove nothing changed. Tested on EF Core 10, both engines: a list filter from a
  request still translates under C# 14 (EF 8 and 9 can't, so EF moves with the framework) and no longer
  uses `OPENJSON` on SQL Server; `ExecuteUpdate`'s new lambda with a conditional setter. Linked from
  `dotnet-core`.

## 0.1.26 — story and production tests for the security skills
- `encryption-patterns`, `pii-masking`, `owasp-aspnetcore`, `secret-management` and `auth-patterns` each
  have a story test and a production test. With them, every installed skill with tested code has both.
- What the tests found:
  - **pii-masking:** a national ID or mobile typed in Arabic-Indic digits (`١٠٨٩٢٣٤٥٦٧`) went through the
    log masking whole: the patterns started with `[12]` and `0?5`. They now list the Arabic-Indic digits.
    And neither masking layer touches an exception: `logger.Error(ex, ...)` sends its message unmasked, so
    log the type, or the message as a property.
  - **encryption-patterns:** a legacy CBC value decrypts in any row (it isn't bound to one), which is the
    reason to re-encrypt legacy rows rather than wait for the next edit.

## 0.1.25 — story and production tests for the API and platform skills
- `api-design`, `localization`, `nginx` and `production-diagnostics` each have a story test and a
  production test. Stories: a third code request told to wait while another patient is served, and a
  crash that shows support only a `traceId`; an Arabic phone's reminder (Arabic text, western-digit
  reference, English where Arabic is missing); a 6 MB scan refused by nginx before the app; an exception
  burst that shows in that minute's heartbeat. On a slim image: the JSON localization pattern works, and
  everything that needs a culture fails loudly.
- `CLAUDE.md`: ideas from other projects are rewritten in our own words and code, tested, and never copied.

## 0.1.24 — two new advisories on System.Security.Cryptography.Pkcs
- CVE-2026-70162 and CVE-2026-84008 (denial of service) affect `System.Security.Cryptography.Pkcs` before
  10.0.13, which `Microsoft.Data.SqlClient` brings in. The zero-vulns gate failed the build as it should;
  the test projects pin 10.0.13 until SqlClient moves. Services on SQL Server need the same pin: run
  `dotnet list package --vulnerable --include-transitive`.

## 0.1.23 — story and production tests for the design and testing skills
- `design-patterns`, `ddd-patterns` and `testing-integration` each have a story test and a production
  test. Stories: a late no-show job can't overwrite a completed visit; a late add to a cancelled order is
  refused and the total holds; a patient guessing the next order numbers gets 404 for each. On a slim
  image: Arabic SMS text, Arabic product names and the orders API on PostgreSQL.

## 0.1.22 — story and production tests for the messaging skills
- `polly-resilience`, `worker-patterns`, `kafka-patterns` and `grpc-patterns` each have a story test and a
  production test (no ICU, no tzdata, UTC). Stories: a payment provider's bad minute (the status check is
  retried, the charge is sent once); a deploy with 20 reminders queued and a failing SMS gateway; a lab
  result parked on the dead-letter topic and replayed after the fix; a zero quantity refused with the
  reason. On a slim image: an Arabic payment reference is percent-encoded, Arabic Kafka keys and values
  flow, and gRPC orders keep UTC timestamps and exact money.

## 0.1.21 — CI pinned to Ubuntu 24.04
- GitHub moves `ubuntu-latest` to Ubuntu 26 from 19 October, three days before the release candidate.
  The workflows now name `ubuntu-24.04`, so the move happens on purpose, in its own change, after 0.2.0.

## 0.1.20 — story and production tests for the data skills
- `efcore-patterns`, `dapper-patterns`, `postgresql-patterns`, `sqlserver-patterns`, `mongodb-patterns`,
  `redis-patterns`, `outbox` and `cqrs-eventsourcing` now each have a story test and a production test (no
  ICU, no tzdata, UTC). Stories: a note edited on two devices; an order cancelled mid-scroll (keyset vs
  OFFSET); a pod dying mid-job and releasing its lock; two scanners counting a new product; a push pod
  killed between sending and saving its place; an OTP limit across three pods; a broker down all morning;
  a mistyped command refused by field.
- What the tests found:
  - **outbox:** the inbox consumer counted bookings by the UTC day, so a 01:30 booking in Riyadh landed on
    the day before. It now counts by the clinic's day.
  - **postgresql-patterns:** on a pod in UTC, `DateTime.Now` reads like UTC but has `Kind=Local`, and Npgsql
    refuses it for `timestamptz` all the same.
  - **sqlserver-patterns:** without ICU the service starts and fails on its first query, not at start-up.
  - **mongodb-patterns:** change streams deliver at least once; the push handler records what it sent by
    `_id`, so a restart doesn't push twice.
## 0.1.19 — the review evals, first runs
- `/full-review` with proven and a plain review with only the house rules both found all twelve planted
  findings, without flagging a distractor; `/health-check` graded the service F with the unmeasured parts
  marked N/A. Results in `tests/evals/full-review/results.md`: the fixture is too easy to tell the two
  apart, so harder findings come next.
- `code-health` 1.1.0: when the package scan can't run (no feed), the direct references are checked
  against published advisories and a confirmed vulnerable package counts in Security. The first run left
  `System.Text.Json` 8.0.4 out of the grade because no tool confirmed it.
- `skill-evals`: a command that launches agents can't be run inside a subagent (it has no tool to start
  them); launch them from the main session. A baseline started inside the repo still has the house rules.
- The fixture's two unplanted real defects (column names that differ between Dapper and EF, a missing
  JwtBearer package) are listed in the cases so they count as found.

## 0.1.18 — evals for /full-review and /health-check
- `tests/evals/full-review/PharmacyRefills`: a small service with twelve planted findings (SQL injection, two
  IDORs, PHI in logs, a swallowed `.Result`, a dual write, an unbounded column, an N+1, `System.Text.Json`
  8.0.4 with CVE-2024-43485...) and three distractors, among them a `FromSql($"...")` call that is
  parameterized. Cases in `tests/evals/full-review/evals.md` and `code-health/evals/evals.md`, run per
  `skill-evals`: a fresh subagent per case, with and without proven-dotnet.

## 0.1.17 — the per-project skills tested
- Most per-project skills now show code that runs in CI, with story and production tests. NATS 2.10 and
  RabbitMQ 4.1 join the CI services; the workflows in `cicd-github-actions` are checked by actionlint and
  shellcheck. `bigquery` and `firebase` stay untested (they need Google's services), and the README says so.
- What the tests found:
  - **signalr:** the hub let any client `JoinGroup("patient-42")` and receive another patient's updates.
    Messages now go to the user. Tested: the Redis backplane across two pods, nothing without it, and a
    connection that fails when the load balancer isn't sticky.
  - **nats:** the old setup (a bare `NatsConnection`) can't publish a record: `NatsException`. Queue
    groups, fan-out, wildcards, request/reply with no responder, and at-most-once delivery tested.
  - **rabbitmq-patterns:** it said to default to `RabbitMQ.Client` and showed only MassTransit. Now
    RabbitMQ.Client 7: quorum queues with a delivery limit and dead-lettering (RabbitMQ 4 drops a message
    after 20 tries without one, tested), confirmed mandatory publishes, manual acks, competing consumers.
  - **cap-library:** CAP creates its schema at every start and starts anyway when that fails; publisher
    confirms are off by default; the default retry count is 50. The schema now comes from a reviewed
    script and the service checks it at start-up.
  - **fhir:** responses are matched to requests by the request header's id (R4), not the bundle
    identifier the skill named; `FhirJsonParser` is obsolete in SDK 6.
  - **firebase:** pruning tokens on `InvalidArgument` would delete every token a malformed message was
    sent to.

## 0.1.16 — testing-architecture tested
- `testing-architecture` 2.0.0. The rules run in CI against two copies of a small service, one laid out
  properly and one with the usual mistakes, with story and production tests. What they found:
  - **A rule that selects no types passes.** A renamed namespace turned every NetArchTest rule into one
    that could never fail. Each rule now checks its selection isn't empty.
  - The package version in the skill (`1.4.0`) doesn't exist; 1.3.2 is the latest. ArchUnitNET was called
    a fork; it's a separate library.
  - Rules now return the failing type names, so a failure says which type broke which rule.
  - Assembly scanning registers whatever implements the interface in the assembly: the CQRS samples
    picked up the architecture samples' handlers until those got interfaces of their own.

## 0.1.15 — openiddict-server tested
- `openiddict-server` 2.0.0, tested end to end against SQL Server: login, the code flow with PKCE,
  reference tokens, introspection by an API, refresh, revocation and logout, with story and production
  tests. What they found:
  - **OpenIddict 6 breaks on .NET 10.** It's built against EF Core 9; on EF Core 10 every bulk revoke
    (logout) throws `TypeLoadException` for `SetPropertyCalls`. Sign-in and refresh still work, so it
    shows up only when someone logs out. The samples use OpenIddict 7; a service that can't move yet
    calls `DisableBulkOperations()`.
  - **SqlClient refuses to connect without ICU** ("Globalization Invariant Mode is not supported"): a SQL
    Server service on a slim image without ICU can't reach its database.
  - **Revoking only the refresh token leaves the access token working**; the skill said otherwise. Logout
    now revokes every token of the user.
  - **Without a token-endpoint handler, a refresh keeps the roles from sign-in** for the refresh token's
    lifetime. The handler reloads them.
  - Identity's string columns were unbounded and its keys GUIDs; now bounded, `int` users, `bigint`
    tokens, and OpenIddict's own JSON columns are the recorded exception. Password rules favour length
    over composition. The company-specific phase notes are gone.

## 0.1.14 — healthcare-compliance tested
- `healthcare-compliance` 2.0.0, its code tested in CI against PostgreSQL, with story and production
  tests. What the rewrite and the tests found:
  - **The PHI audit was written beside the save, before it ran:** a failed save still left an audit row
    saying it happened, and new records were audited with id 0. The audit rows now go into the same
    `SaveChanges` (HiLo keys give new records their id first), they name the changed columns but never the
    values, and the service's database role can add audit rows but not change or delete them (tested:
    `42501`).
  - **Consent was checked for the signed-in user**, who is the doctor, not the patient. The command now
    names the patient whose consent is checked.
  - **Data residency was enforced from a request header** the client could leave out. It's now checked in
    configuration at start-up.
  - Legal references cut to the ones verified against published sources (PDPL Articles 4 and 29, the
    72-hour breach notification and five-year processing records from the Implementing Regulations,
    HIPAA §164.312 and §164.316(b)(2)). The invented SDAIA client and the "HIPAA recommended 15 minutes"
    are gone.

## 0.1.13 — testing-tdd tested
- `testing-tdd` 2.0.0. Its examples are now real tests of a small booking aggregate, run in CI with story
  and production tests. What they found:
  - **AutoMoq gives `TimeProvider` the real clock:** it mocks the abstract class and calls the base, so
    a handler built by AutoMoq runs on today's date and a test pinned to a date breaks once it passes.
    The `AutoMoqData` attribute now injects one fixed `FakeTimeProvider`.
  - **AutoFixture's own `FakeTimeProvider` moves on every read:** it fills `AutoAdvanceAmount`.
  - **A unit test that sets a culture crashes without ICU** (`new CultureInfo("en-US")`): unit tests
    don't set one.
  - FluentAssertions resolves 7.x under `[7.0.0,8.0.0)` (tested), and a `Verify(Times.Once)` on the
    save catches a handler that saves twice.
- The skill no longer states a CI-specific coverage setup as a general rule.

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
