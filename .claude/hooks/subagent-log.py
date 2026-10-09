#!/usr/bin/env python3
"""
SubagentStop hook (async): one line per finished subagent in ~/.claude/proven/agent-runs.log, so we can see
which agents actually run and whether they use the Roslyn server (#13). Records names and counts only:
no prompts, no tool inputs or outputs, no code, no paths inside the repo.

    {"ts": "2026-10-07T09:12:03Z", "agent": "code-reviewer", "tools": 31, "roslyn": 9,
     "roslyn_tools": {"find_callers": 5, "find_references": 4}}

The hook's input field names aren't spelled out in the docs we have, so several likely names are tried.
A missing transcript still logs the agent with tools = null. The file is trimmed to its newest half
once it passes 1 MB. Any error is swallowed: a logging hook must never disturb the session.
"""
import json
import sys
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path

LOG = Path.home() / ".claude" / "proven" / "agent-runs.log"
MAX_BYTES = 1_000_000
ROSLYN = "mcp__proven-roslyn__"


def first(event, *keys):
    for key in keys:
        value = event.get(key)
        if value:
            return value
    return None


def count_tools(transcript_path):
    names = Counter()
    with open(transcript_path, encoding="utf-8", errors="replace") as f:
        for line in f:
            try:
                entry = json.loads(line)
            except ValueError:
                continue
            message = entry.get("message") if isinstance(entry, dict) else None
            content = message.get("content") if isinstance(message, dict) else None
            if isinstance(content, list):
                for block in content:
                    if isinstance(block, dict) and block.get("type") == "tool_use" and block.get("name"):
                        names[block["name"]] += 1
    return names


def record(event):
    agent = first(event, "agent_type", "subagent_type", "agent_name", "agent") or "unknown"
    transcript = first(event, "agent_transcript_path", "subagent_transcript_path", "transcript_path")
    line = {"ts": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"), "agent": agent,
            "tools": None, "roslyn": None, "roslyn_tools": {}}
    if transcript and Path(transcript).is_file():
        names = count_tools(transcript)
        roslyn = {n[len(ROSLYN):]: c for n, c in names.items() if n.startswith(ROSLYN)}
        line.update(tools=sum(names.values()), roslyn=sum(roslyn.values()), roslyn_tools=roslyn)
    return line


def append(line):
    LOG.parent.mkdir(parents=True, exist_ok=True)
    if LOG.exists() and LOG.stat().st_size > MAX_BYTES:
        lines = LOG.read_text(encoding="utf-8").splitlines()
        LOG.write_text("\n".join(lines[len(lines) // 2:]) + "\n", encoding="utf-8")
    with open(LOG, "a", encoding="utf-8") as f:
        f.write(json.dumps(line) + "\n")


def main():
    try:
        append(record(json.load(sys.stdin)))
    except Exception:   # noqa: BLE001 - never disturb the session
        pass


if __name__ == "__main__":
    main()
