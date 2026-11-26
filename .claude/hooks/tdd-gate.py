#!/usr/bin/env python3
"""The TDD gate for tdd-guide: production code waits for a failing test, and the work ends green. Wired in
the agent's own frontmatter, so it runs inside that agent and nowhere else. Its `done` mode is the lighter
gate for the main session, wired in settings.json.

    {PYTHON} {HOOKS}/tdd-gate.py pre     PreToolUse  Write|Edit|MultiEdit
    {PYTHON} {HOOKS}/tdd-gate.py post    PostToolUse Bash|Write|Edit|MultiEdit, PostToolUseFailure Bash
    {PYTHON} {HOOKS}/tdd-gate.py stop    Stop (in an agent's frontmatter Claude Code runs it as SubagentStop)
    {PYTHON} {HOOKS}/tdd-gate.py done    Stop, in the main session (settings.json), with post on its edits and runs

- pre: an edit to a production `.cs` file is refused until a `dotnet test` run in this agent has gone red
  (a failing test, or a test that doesn't compile yet). Tests, `story.md`, projects and settings are
  never held back. Tests qa-engineer wrote beforehand count: run them, see them fail, then implement.
- post: records each `dotnet test` result, and each production edit (which makes the last result stale).
- stop: once production code was edited, the agent can't finish until the last test run after the last
  edit is green. It asks three times, then lets the agent finish so it can report what is still red.
- done: the main session, where the developer is there to judge. When production code changed since the
  last green test run, the turn is sent back once, to run the tests or say why not; then it ends. The next
  edit or test run makes it ask again. Nothing is held back before the code is written.
  It also holds a handover: an answer that gives push commands or an MR's text while production code
  changed (in any agent of the session) since code-reviewer last ran is sent back once, to run
  /full-review or say why not. A change that reaches review unreviewed is what reviewers then have to catch.

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
REVIEWERS = {"code-reviewer"}
# What a handover to the developer looks like: commands that publish the branch, or an MR's own text.
HANDOVER = re.compile(r"\bgit push\b|\bglab mr create\b|\bMR (?:description|title)s?\b|\bmerge request description\b"
                      r"|\bopen (?:the|an|two|\d+) (?:new )?MRs?\b", re.I)


def state_path(event: dict) -> Path:
    key = f"{event.get('session_id', '')}|{event.get('agent_id') or event.get('agent_type') or 'main'}"
    folder = Path(os.environ.get("PROVEN_TDD_GATE_DIR") or Path(tempfile.gettempdir()) / "proven-tdd-gate")
    folder.mkdir(parents=True, exist_ok=True)
    return folder / (hashlib.sha256(key.encode()).hexdigest()[:24] + ".json")


def load(path: Path) -> dict:
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return {"unlocked": False, "edited": False, "stale": False, "last": None, "stops": 0, "reminded": False}


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


def review_path(event: dict) -> Path:
    """One record per session, whichever agent made the change."""
    return state_path({"session_id": event.get("session_id", ""), "agent_type": "review"})


def note_review(event: dict) -> None:
    tool, tool_input = event.get("tool_name"), event.get("tool_input") or {}
    if tool in EDITS and production_file(event):
        change = {"unreviewed": True, "reminded": False}
    elif tool in ("Agent", "Task") and tool_input.get("subagent_type") in REVIEWERS:
        change = {"unreviewed": False}
    else:
        return
    path = review_path(event)
    save(path, {**load_review(path), **change})


def load_review(path: Path) -> dict:
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return {"unreviewed": False, "reminded": False}


def last_answer(transcript: str) -> str:
    """The text the session wrote since the developer's last message."""
    try:
        lines = Path(transcript).read_text(encoding="utf-8").splitlines()
    except (OSError, TypeError, ValueError):
        return ""
    texts: list[str] = []
    for line in lines:
        try:
            row = json.loads(line)
        except ValueError:
            continue
        content = (row.get("message") or {}).get("content")
        if row.get("type") == "user":
            asked = isinstance(content, str) or any(isinstance(b, dict) and b.get("type") == "text" for b in content or [])
            if asked:
                texts = []
        elif row.get("type") == "assistant" and isinstance(content, list):
            texts += [b.get("text", "") for b in content if isinstance(b, dict) and b.get("type") == "text"]
    return "\n".join(texts)


def post(event: dict, state: dict) -> None:
    note_review(event)
    tool = event.get("tool_name")
    if tool in EDITS and production_file(event):
        state["edited"], state["stale"], state["reminded"] = True, True, False
    elif tool == "Bash" and DOTNET_TEST.search((event.get("tool_input") or {}).get("command") or ""):
        text = output_of(event)
        if RED.search(text):
            state["last"], state["unlocked"], state["stale"], state["reminded"] = "red", True, False, False
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


def done(event: dict, state: dict) -> int:
    if event.get("stop_hook_active"):
        return 0  # said once: the developer reads the answer and decides
    if state["edited"] and not (state["last"] == "green" and not state["stale"]) and not state.get("reminded"):
        state["reminded"] = True
        why = ("C# changed and no test has run since" if state["stale"] else "the last `dotnet test` was red")
        print(f"Done gate: {why}. Run the tests that cover the change (`dotnet test`) before calling it done, "
              "or say in your answer which tests you didn't run and why.", file=sys.stderr)
        return 2
    return review_gate(event)


def review_gate(event: dict) -> int:
    path = review_path(event)
    review = load_review(path)
    if not review["unreviewed"] or review["reminded"] or not HANDOVER.search(last_answer(event.get("transcript_path"))):
        return 0
    save(path, {**review, "reminded": True})
    print("Review gate: this answer hands over push commands or an MR, and production C# changed since "
          "code-reviewer last looked. Run `/full-review --diff <base>` on it first, or say in the answer why "
          "it can't run.", file=sys.stderr)
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
    if mode in ("stop", "done"):
        code = stop(event, state) if mode == "stop" else done(event, state)
        save(path, state)
        return code
    print(f"TDD gate: unknown mode '{mode}' (pre, post, stop or done)", file=sys.stderr)
    return 2 if mode == "" else 0


if __name__ == "__main__":
    sys.exit(main())
