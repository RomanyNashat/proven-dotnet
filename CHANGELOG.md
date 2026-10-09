# Changelog

Versions are X.Y.Z: Z a fix or build step, Y a finished feature, X only when updating needs something
from you. Until v1.0.0 nothing is meant to be installed yet.

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
