#!/usr/bin/env python3
"""Condense the Claude Code sessions that worked on one repo — input for the /handover analyst.

Read-only. Reads ~/.claude/projects/*/*.jsonl (Claude Code's own transcripts) and prints, per session,
only what was SAID: your messages and Claude's replies. Tool output, file contents, subagent chatter
and system reminders are dropped — they are most of each file and none of the story.

Claude Code deletes transcripts after `cleanupPeriodDays` (30 by default; the proven-dotnet settings set 120), so older
sessions may simply not exist.

A session counts for the repo when its working folder is the repo (or inside it), or when it ran in a
parent folder and its tool calls touched the repo.

Safety: long digit runs (national IDs, phone numbers, MRNs) and email addresses are masked before
printing. That is a net, not a guarantee — the analyst must still never copy data values.

Usage:
    python extract_sessions.py --repo C:/src/work/payment-service --since 2026-06-01 [--max-chars 60000]
"""
import argparse
import glob
import json
import os
import re
import sys

REMINDER = re.compile(r"<(system-reminder|local-command-stdout|local-command-stderr)>.*?</\1>", re.S)
LONG_NUMBER = re.compile(r"\b\d{9,}\b")
EMAIL = re.compile(r"\b[\w.+-]+@[\w-]+\.[\w.-]+\b")
EDIT_TOOLS = {"Edit", "Write", "MultiEdit", "NotebookEdit"}


def norm(path):
    p = (path or "").replace("\\", "/").rstrip("/")
    return p.lower() if os.name == "nt" or re.match(r"^[a-zA-Z]:/", p) else p


def mask(text):
    return EMAIL.sub("[email]", LONG_NUMBER.sub("[number]", text))


def clip(text, limit):
    text = " ".join(text.split())
    return text if len(text) <= limit else text[:limit] + " …"


def texts(content):
    """Plain text blocks of a message; tool results / tool calls / thinking are skipped."""
    if isinstance(content, str):
        return [content]
    return [b.get("text", "") for b in content or [] if isinstance(b, dict) and b.get("type") == "text"]


def tool_inputs(content):
    if not isinstance(content, list):
        return []
    return [b for b in content if isinstance(b, dict) and b.get("type") == "tool_use"]


def read_session(path, repo, since):
    repo_n, repo_name = norm(repo), os.path.basename(norm(repo))
    turns, files, touched, inside, start = [], set(), False, False, None
    with open(path, encoding="utf-8", errors="replace") as fh:
        for line in fh:
            try:
                e = json.loads(line)
            except json.JSONDecodeError:
                continue
            if e.get("isSidechain") or e.get("isMeta") or e.get("type") not in ("user", "assistant"):
                continue
            ts = (e.get("timestamp") or "")[:10]
            start = start or ts
            cwd = norm(e.get("cwd"))
            if cwd == repo_n or cwd.startswith(repo_n + "/"):
                inside = True
            msg = e.get("message") or {}
            parent = bool(cwd) and repo_n.startswith(cwd + "/")     # session ran in a folder above the repo
            for tool in tool_inputs(msg.get("content")):
                blob = json.dumps(tool.get("input", {})).replace("\\\\", "/").lower()
                if parent and repo_name.lower() in blob:
                    touched = True
                if tool.get("name") in EDIT_TOOLS:
                    fp = norm(str(tool.get("input", {}).get("file_path", "")))
                    if fp.startswith(repo_n + "/"):
                        files.add(fp[len(repo_n) + 1:])
            for t in texts(msg.get("content")):
                t = REMINDER.sub("", t).strip()
                if not t or t.startswith("<command-") and "<command-args></command-args>" in t and len(t) < 200:
                    continue
                who = "You" if e["type"] == "user" else "Claude"
                turns.append(f"- **{who}:** {mask(clip(t, 1200 if who == 'You' else 1500))}")
    if not (inside or touched) or not start or start < since or not turns:
        return None
    where = "in the repo" if inside else "from a parent folder"
    header = f"## Session {start} ({where}, {len(turns)} messages)"
    if files:
        header += "\nFiles edited: " + ", ".join(sorted(files)[:25]) + (" …" if len(files) > 25 else "")
    return start, header + "\n" + "\n".join(turns)


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--repo", required=True)
    p.add_argument("--since", required=True, help="YYYY-MM-DD")
    p.add_argument("--max-chars", type=int, default=60000)
    p.add_argument("--projects-dir", default=os.path.join(os.path.expanduser("~"), ".claude", "projects"))
    a = p.parse_args()

    files = glob.glob(os.path.join(a.projects_dir, "*", "*.jsonl"))
    sessions = sorted(filter(None, (read_session(f, os.path.abspath(a.repo), a.since) for f in files)), reverse=True)

    out, used, skipped = [], 0, 0
    for _, block in sessions:                         # newest first: recent context matters most
        if used + len(block) > a.max_chars:
            skipped += 1
            continue
        out.append(block)
        used += len(block)

    oldest = min((s[0] for s in sessions), default=None)
    print(f"# Sessions for {os.path.basename(os.path.abspath(a.repo))} since {a.since}: {len(sessions)} found"
          + (f", oldest {oldest}" if oldest else "")
          + (f" — {skipped} older one(s) left out to stay under {a.max_chars} chars" if skipped else ""))
    if not sessions:
        print("No surviving sessions for this repo in the period (Claude Code deletes old transcripts).")
    print("\n\n".join(out))


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    main()
