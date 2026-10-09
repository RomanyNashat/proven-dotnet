#!/usr/bin/env python3
"""
PreToolUse guard, registered only for mcp__proven-roslyn__rename_symbol (so it costs nothing on other
tools and no per-turn launch).

The Roslyn server has the developer's main checkout loaded. An agent running isolated in a git
worktree (.claude/worktrees/<name>) that calls rename_symbol with preview=false would therefore edit
the main checkout's files, not the worktree's: the isolation would be silently broken. This hook denies
that one call; a preview (the default) is still allowed, since it writes nothing.
"""
import json
import sys


def decide(event):
    cwd = (event.get("cwd") or "").replace("\\", "/").rstrip("/") + "/"
    tool_input = event.get("tool_input") or {}
    applies = tool_input.get("preview") is False or str(tool_input.get("preview")).lower() == "false"
    if "/.claude/worktrees/" not in cwd or not applies:
        return None
    return {
        "hookSpecificOutput": {
            "hookEventName": "PreToolUse",
            "permissionDecision": "deny",
            "permissionDecisionReason": (
                "rename_symbol would edit the main checkout, not this worktree (the Roslyn server has the "
                "main checkout loaded). Make the rename as file edits here, or preview it and apply it "
                "after the developer approves."
            ),
        }
    }


def main() -> None:
    try:
        event = json.load(sys.stdin)
    except (json.JSONDecodeError, ValueError):
        return   # never block on a malformed event
    result = decide(event)
    if result:
        print(json.dumps(result))


if __name__ == "__main__":
    main()
