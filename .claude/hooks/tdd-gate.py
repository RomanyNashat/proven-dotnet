#!/usr/bin/env python3
"""The TDD gate for tdd-guide: production code waits for a failing test, and the work ends green. Wired in
the agent's own frontmatter, so it runs inside that agent and nowhere else.

    {PYTHON} {HOOKS}/tdd-gate.py pre     PreToolUse  Write|Edit|MultiEdit
    {PYTHON} {HOOKS}/tdd-gate.py post    PostToolUse Bash|Write|Edit|MultiEdit, PostToolUseFailure Bash
    {PYTHON} {HOOKS}/tdd-gate.py stop    Stop (in an agent's frontmatter Claude Code runs it as SubagentStop)

- pre: an edit to a production `.cs` file is refused until a `dotnet test` run in this agent has gone red
  (a failing test, or a test that doesn't compile yet). Tests, `story.md`, projects and settings are
  never held back. Tests qa-engineer wrote beforehand count: run them, see them fail, then implement.
- post: records each `dotnet test` result, and each production edit (which makes the last result stale).
- stop: once production code was edited, the agent can't finish until the last test run after the last
  edit is green. It asks three times, then lets the agent finish so it can report what is still red.

State lives in the temp folder, one file per agent run. pre fails closed on input it can't read; post
and stop let it through, since they guard nothing on their own.
"""
from __future__ import annotations

import hashlib
import importlib.util
import json
import os
import re
import sys
import tempfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
_spec = importlib.util.spec_from_file_location("read_only_guard", HERE / "read-only-guard.py")
_guard = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_guard)
is_test_path, relative_to = _guard.is_test_path, _guard.relative_to

MAX_STOPS = 3
DOTNET_TEST = re.compile(r"(^|[\s;&|(])dotnet(\.exe)?\s+test\b")
RED = re.compile(r"\bFailed!|\bTest Run Failed\b|\berror CS\d{4}\b|\bBuild FAILED\b|\bFailed:\s+[1-9]|\[FAIL\]")
GREEN = re.compile(r"\bPassed!|\bTest Run Successful\b")
EDITS = {"Write", "Edit", "MultiEdit"}


def state_path(event: dict) -> Path:
    key = f"{event.get('session_id', '')}|{event.get('agent_id') or event.get('agent_type') or 'main'}"
    folder = Path(os.environ.get("PROVEN_TDD_GATE_DIR") or Path(tempfile.gettempdir()) / "proven-tdd-gate")
    folder.mkdir(parents=True, exist_ok=True)
    return folder / (hashlib.sha256(key.encode()).hexdigest()[:24] + ".json")


def load(path: Path) -> dict:
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return {"unlocked": False, "edited": False, "stale": False, "last": None, "stops": 0}


def save(path: Path, state: dict) -> None:
    path.write_text(json.dumps(state), encoding="utf-8")


def production_file(event: dict) -> str | None:
    """The file's path inside the working folder when it is production C#, else None."""
    tool_input = event.get("tool_input") or {}
    path = tool_input.get("file_path") or ""
    rel = relative_to(path, event.get("cwd") or "")
    if not rel.lower().endswith(".cs") or is_test_path(rel):
        return None
    return rel


def output_of(event: dict) -> str:
    """A passing command's output is in tool_response; a failing one (dotnet test exits 1 when a test
    fails) reaches PostToolUseFailure instead, its output in `error` after "Exit code 1"."""
    response = event.get("tool_response")
    parts = [str(event.get("error") or "")]
    if isinstance(response, dict):
        parts += [str(response.get(k) or "") for k in ("stdout", "stderr", "output")]
    elif response:
        parts.append(str(response))
    return "\n".join(parts)


def pre(event: dict, state: dict) -> int:
    if event.get("tool_name") not in EDITS:
        return 0
    rel = production_file(event)
    if rel is None or state["unlocked"]:
        return 0
    print(f"TDD gate: no failing test yet in this task, so {rel} waits. Write or change the test for it, run "
          "`dotnet test` and see it fail (a test that doesn't compile yet counts), then make it pass. "
          "Tests written beforehand count too: run them first.", file=sys.stderr)
    return 2


def post(event: dict, state: dict) -> None:
    tool = event.get("tool_name")
    if tool in EDITS and production_file(event):
        state["edited"], state["stale"] = True, True
    elif tool == "Bash" and DOTNET_TEST.search((event.get("tool_input") or {}).get("command") or ""):
        text = output_of(event)
        if RED.search(text):
            state["last"], state["unlocked"], state["stale"] = "red", True, False
        elif GREEN.search(text):
            state["last"], state["stale"] = "green", False


def stop(event: dict, state: dict) -> int:
    if not state["edited"] or (state["last"] == "green" and not state["stale"]):
        return 0
    if state["stops"] >= MAX_STOPS:
        return 0  # asked enough: let it finish and say what is still red
    state["stops"] += 1
    why = "production code changed after the last test run" if state["stale"] else "the last test run is red"
    print(f"TDD gate: {why}. Run `dotnet test` and finish green, or say which test is still red and why.",
          file=sys.stderr)
    return 2


def main() -> int:
    mode = sys.argv[1] if len(sys.argv) > 1 else ""
    try:
        event = json.load(sys.stdin)
        if not isinstance(event, dict):
            raise ValueError
    except ValueError:
        if mode == "pre":
            print("TDD gate: the hook input wasn't JSON, so the edit was refused", file=sys.stderr)
            return 2
        return 0
    path = state_path(event)
    state = load(path)
    if mode == "pre":
        return pre(event, state)
    if mode == "post":
        post(event, state)
        save(path, state)
        return 0
    if mode == "stop":
        code = stop(event, state)
        save(path, state)
        return code
    print(f"TDD gate: unknown mode '{mode}' (pre, post or stop)", file=sys.stderr)
    return 2 if mode == "" else 0


if __name__ == "__main__":
    sys.exit(main())
