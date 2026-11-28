#!/usr/bin/env python3
"""Which skills, agents, slash commands and MCP servers this machine's Claude Code sessions used, counted
from the local transcripts (~/.claude/projects/*/*.jsonl). It reads only, sends nothing anywhere, and
prints names and counts: never a prompt, a path or anything a session said.

    python usage.py                     # the last 30 days, a table
    python usage.py --days 90 --json

Beside the counts: the installed skills no session used in the window (candidates to merge, trim or
describe better), and symbol questions answered by text search on C# files against `proven-roslyn` calls.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from collections import defaultdict
from datetime import date, datetime, timedelta, timezone
from pathlib import Path

COMMAND = re.compile(r"<command-name>\s*(/[\w:.-]+)\s*</command-name>")
TEXT_SEARCH = re.compile(r"(?:^|[\s;&|(])(?:grep|rg|findstr|Select-String)\b")
CS_TARGET = re.compile(r"\.cs\b|--type[= ]cs\b|-t\s*cs\b|--include[= ]['\"]?\*\.cs")


def events(path: Path):
    try:
        lines = path.read_text(encoding="utf-8", errors="replace").splitlines()
    except OSError:
        return
    for line in lines:
        try:
            row = json.loads(line)
        except ValueError:
            continue
        if isinstance(row, dict):
            yield row


def when(row: dict) -> date | None:
    try:
        return datetime.fromisoformat(str(row.get("timestamp", "")).replace("Z", "+00:00")).astimezone(timezone.utc).date()
    except ValueError:
        return None


def blocks(row: dict) -> list:
    content = (row.get("message") or {}).get("content")
    if isinstance(content, str):
        return [{"type": "text", "text": content}]
    return [b for b in content or [] if isinstance(b, dict)]


def count(home: Path, since: date) -> dict:
    uses: dict[tuple[str, str], int] = defaultdict(int)
    sessions: dict[tuple[str, str], set] = defaultdict(set)
    last: dict[tuple[str, str], date] = {}
    symbol = {"roslyn": 0, "text_search_on_cs": 0}
    files = sorted((home / ".claude" / "projects").glob("*/*.jsonl"))
    for path in files:
        for row in events(path):
            day = when(row)
            if day is None or day < since:
                continue
            for b in blocks(row):
                key = None
                if b.get("type") == "text" and row.get("type") == "user":
                    m = COMMAND.search(b.get("text") or "")
                    key = ("command", m.group(1)) if m else None
                elif b.get("type") == "tool_use":
                    name, tool_input = b.get("name") or "", b.get("input") or {}
                    if name == "Skill" and tool_input.get("skill"):
                        key = ("skill", str(tool_input["skill"]))
                    elif name in ("Agent", "Task"):
                        key = ("agent", str(tool_input.get("subagent_type") or "general-purpose"))
                    elif name.startswith("mcp__"):
                        server = name.split("__")[1]
                        key = ("mcp", server)
                        if server in ("proven-roslyn", "ecc-roslyn"):
                            symbol["roslyn"] += 1
                    elif name == "Bash":
                        command = str(tool_input.get("command") or "")
                        if TEXT_SEARCH.search(command) and CS_TARGET.search(command):
                            symbol["text_search_on_cs"] += 1
                    elif name == "Grep" and (str(tool_input.get("glob") or "").endswith(".cs") or tool_input.get("type") == "cs"):
                        symbol["text_search_on_cs"] += 1
                if key:
                    uses[key] += 1
                    sessions[key].add(path.stem)
                    last[key] = max(last.get(key, day), day)
    used = [{"kind": k, "name": n, "uses": uses[(k, n)], "sessions": len(sessions[(k, n)]), "last": last[(k, n)].isoformat()}
            for k, n in sorted(uses, key=lambda key: (-uses[key], key))]
    installed = sorted(p.parent.name for p in (home / ".claude" / "skills").glob("*/SKILL.md"))
    used_skills = {n for k, n in uses if k == "skill"}
    return {"since": since.isoformat(), "transcripts": len(files), "used": used,
            "never_used_skills": [s for s in installed if s not in used_skills], "symbol_questions": symbol}


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--days", type=int, default=30)
    ap.add_argument("--home", default=str(Path.home()))
    ap.add_argument("--today", help="YYYY-MM-DD, for tests")
    ap.add_argument("--json", action="store_true")
    args = ap.parse_args(argv)
    today = date.fromisoformat(args.today) if args.today else datetime.now(timezone.utc).date()
    result = count(Path(args.home), today - timedelta(days=args.days))
    if args.json:
        print(json.dumps(result, indent=1))
        return 0
    if not result["transcripts"]:
        print("No session transcripts under ~/.claude/projects.")
        return 0
    print(f"Since {result['since']}, {result['transcripts']} transcripts\n")
    print("| Kind | Name | Uses | Sessions | Last |\n|---|---|---|---|---|")
    for r in result["used"]:
        print(f"| {r['kind']} | {r['name']} | {r['uses']} | {r['sessions']} | {r['last']} |")
    s = result["symbol_questions"]
    print(f"\nSymbol questions: {s['roslyn']} through Roslyn, {s['text_search_on_cs']} by text search on C# files.")
    if result["never_used_skills"]:
        print(f"Installed skills no session used: {', '.join(result['never_used_skills'])}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
