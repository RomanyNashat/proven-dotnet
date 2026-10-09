#!/usr/bin/env python3
"""Checks the installable content in .claude/ before it ships.

Fails on:
  - core.md over 200 lines (it loads in every session; adherence drops as it grows);
  - a .ps1 file that isn't pure ASCII (Windows PowerShell 5.1 reads a BOM-less file as Windows-1252);
  - a read-only agent (reviewers, analysts) that has a tool able to change files;
  - session history kept for less than 120 days (/handover reads it), or a handover helper script that
    could change a repo.
Also fails on any skill or command the content mentions that doesn't exist.
"""
import pathlib
import re
import sys

root = pathlib.Path(__file__).resolve().parents[1]
claude = root / ".claude"
problems, missing = [], {}

core = claude / "core.md"
if core.exists() and len(core.read_text(encoding="utf-8").splitlines()) > 200:
    problems.append(f"core.md has {len(core.read_text(encoding='utf-8').splitlines())} lines; keep it under 200")

for ps1 in root.rglob("*.ps1"):
    if ".git" in ps1.parts:
        continue
    try:
        ps1.read_bytes().decode("ascii")
    except UnicodeDecodeError:
        problems.append(f"{ps1.relative_to(root)} isn't pure ASCII")

READ_ONLY_AGENTS = ["code-reviewer", "security-reviewer", "dba-reviewer", "compliance-auditor",
                    "health-analyst", "pattern-analyst", "handover-repo-analyst"]
for name in READ_ONLY_AGENTS:
    agent = claude / "agents" / f"{name}.md"
    if not agent.exists():
        continue
    m = re.search(r"^tools:\s*(.+)$", agent.read_text(encoding="utf-8"), re.M)
    tools = {t.strip() for t in (m.group(1) if m else "").split(",")}
    for bad in sorted(tools & {"Write", "Edit", "MultiEdit", "NotebookEdit", "Agent"}):
        problems.append(f"agents/{name}.md is read-only but has {bad}")
analyst = claude / "agents" / "handover-repo-analyst.md"
if analyst.exists() and "git -C" not in analyst.read_text(encoding="utf-8"):
    problems.append("handover-repo-analyst: git commands must be scoped with git -C")

import json
settings = json.loads((claude / "settings.json").read_text(encoding="utf-8"))
days = settings.get("cleanupPeriodDays", 30)
if not isinstance(days, int) or days < 120:
    problems.append(f"settings cleanupPeriodDays={days}; /handover needs at least 120 days of sessions")
scripts = claude / "skills" / "handover" / "scripts"
for script in sorted(scripts.glob("*.py")) if scripts.is_dir() else []:
    src = script.read_text(encoding="utf-8")
    for word in ('"push"', '"commit"', '"fetch"', '"checkout"', '"reset"'):
        if word in src:
            problems.append(f"{script.name} runs git {word}; the handover scripts only read")

skills = {p.name for p in (claude / "skills").iterdir()} if (claude / "skills").is_dir() else set()
commands = {p.stem for p in (claude / "commands").glob("*.md")} if (claude / "commands").is_dir() else set()
commands |= {"compact", "introspect", "metrics"}   # Claude Code built-ins and URL paths, not our commands
for md in sorted(claude.rglob("*.md")):
    text = md.read_text(encoding="utf-8")
    rel = md.relative_to(root).as_posix()
    names = set(re.findall(r"skills/([a-z0-9-]+)/", text)) | set(re.findall(r"`([a-z0-9-]+)` skill", text))
    for name in names - skills:
        missing.setdefault(f"skill {name}", set()).add(rel)
    for name in set(re.findall(r"`/([a-z][a-z0-9-]+)`", text)) - commands:
        missing.setdefault(f"command /{name}", set()).add(rel)

version = (root / "VERSION").read_text(encoding="utf-8").strip()
csproj = root / "tools" / "proven-roslyn-mcp" / "ProvenRoslynMcp.csproj"
if csproj.exists():
    stamped = re.search(r"<Version>([^<]+)</Version>", csproj.read_text(encoding="utf-8"))
    if not stamped or stamped.group(1) != version:
        problems.append(f"ProvenRoslynMcp.csproj <Version> is {stamped.group(1) if stamped else 'missing'}, VERSION is {version}")
strict = True   # every reference resolves since 0.1.7; keep it that way
for what, where in sorted(missing.items()):
    msg = f"{what} is mentioned but not here yet ({', '.join(sorted(where))})"
    print(f"::{'error' if strict else 'notice'} title=Missing reference::{msg}")
    if strict:
        problems.append(msg)

for p in problems:
    print(f"::error title=Content check::{p}")
print(f"content check: {len(problems)} problem(s), {len(missing)} reference(s) still to bring in")
sys.exit(1 if problems else 0)
