# Changelog

Versions are X.Y.Z: Z a fix or build step, Y a finished feature, X only when updating needs something
from you. Until v1.0.0 nothing is meant to be installed yet.

## 0.1.2 — the tested skills
- 23 skills whose code runs in CI against real dependencies: API design, auth, CQRS, Dapper, DDD,
  EF Core, encryption, gRPC, Kafka, localization, MongoDB, nginx, outbox, OWASP, PII masking, Polly,
  PostgreSQL, Redis, secret management, SQL Server, integration testing, workers, design patterns.
- `tests/SkillSamples.Tests`: the code the skills show, run against PostgreSQL 17, SQL Server 2022,
  MongoDB 7 (replica set), Redis 7, Kafka 3.9 and nginx, in two parallel CI jobs. A skill marks each
  tested block with `<!-- sample: path -->`, and CI fails if the block and the file drift apart.

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
