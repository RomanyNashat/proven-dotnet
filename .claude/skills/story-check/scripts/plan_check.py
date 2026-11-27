#!/usr/bin/env python3
"""Checks a plan before any code: its scope against the story, its design against what the repo already
has, and its API as the callers (the mobile team) will see it.

    python plan_check.py plan.md --story story.md --repo .

What the plan holds (planner's output format):
- each task has `**Serves**: AC1, AC3`, or `**Serves**: enabler: <why the story needs it>`;
- `## Builds on`: `- uses: path:line `code on that line` — what it gives`, and `- new: <kind> <Name> —
  because <why nothing there fits>`;
- `## API for its callers`, when the plan adds or changes an endpoint: an example request or response in a
  code block, an `errors:` line, an `old app versions:` line.

It prints `Plan check: READY`, or `NOT READY` with each gap numbered, and exits 1 when not ready.
"""
from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

TASK = re.compile(r"^### Task (\d+)\b", re.M)
SERVES = re.compile(r"^\*\*Serves\*\*:\s*(.*)$", re.M)
CRITERION = re.compile(r"\bAC(\d+)\b")
STORY_CRITERION = re.compile(r"\bAC(\d+):")
USES = re.compile(r"^-\s*uses:\s*(?P<path>[^\s:]+):(?P<line>\d+)\s+`(?P<code>[^`]+)`", re.M)
NEW = re.compile(r"^-\s*new:\s*(?P<what>.+?)\s*(?:—|--|-\s)\s*(?P<why>.*)$|^-\s*new:\s*(?P<bare>.+)$", re.M)
ENDPOINT = re.compile(r"\b(?:GET|POST|PUT|PATCH|DELETE)\s+/\S+|\bendpoints?\b|\bcontrollers?\b", re.I)
DEFINES = r"\b(?:class|record|interface|enum|struct)\s+{name}\b|\bTABLE\s+(?:\[?\w+\]?\.)?\[?{name}\]?(?:\s|\(|$)"


def section(text: str, title: str) -> str | None:
    m = re.search(rf"^## {re.escape(title)}\s*$(.*?)(?=^## |\Z)", text, re.M | re.S)
    return m.group(1) if m else None


def tasks(text: str) -> list[tuple[str, str]]:
    starts = list(TASK.finditer(text))
    found = []
    for i, m in enumerate(starts):
        end = starts[i + 1].start() if i + 1 < len(starts) else len(text)
        block = re.split(r"^## ", text[m.start():end], maxsplit=1, flags=re.M)[0]
        found.append((m.group(1), block))
    return found


def check_scope(text: str, story: str) -> list[str]:
    gaps = []
    criteria = set(STORY_CRITERION.findall(story))
    served: set[str] = set()
    for number, block in tasks(text):
        m = SERVES.search(block)
        value = m.group(1).strip() if m else ""
        if value.lower().startswith("enabler:"):
            if not value[len("enabler:"):].strip():
                gaps.append(f"Task {number} is an enabler with no reason: say what in the story needs it")
            continue
        names = CRITERION.findall(value)
        if not names:
            gaps.append(f"Task {number} serves no criterion: name the AC it serves, mark it `enabler: <why>`, or drop it")
        for n in names:
            if n not in criteria:
                gaps.append(f"Task {number} names AC{n}, which the story doesn't have")
        served.update(n for n in names if n in criteria)
    gaps += [f"AC{n} has no task" for n in sorted(criteria - served, key=int)]
    return gaps


def defined_at(repo: Path, name: str) -> str | None:
    pattern = re.compile(DEFINES.format(name=re.escape(name)), re.I | re.M)
    for path in sorted(p for ext in ("*.cs", "*.sql") for p in repo.rglob(ext)):
        if any(part in ("bin", "obj", ".git", "node_modules") for part in path.parts):
            continue
        try:
            lines = path.read_text(encoding="utf-8-sig", errors="replace").splitlines()
        except OSError:
            continue
        for i, line in enumerate(lines, 1):
            if pattern.search(line):
                return f"{path.relative_to(repo).as_posix()}:{i}"
    return None


def check_design(text: str, repo: Path) -> list[str]:
    body = section(text, "Builds on")
    if body is None or not body.strip():
        return ["no `## Builds on`: name what the repo already has that this uses (`uses: path:line `code``), "
                "and what is new and why"]
    gaps = []
    for m in USES.finditer(body):
        target = repo / m["path"]
        try:
            line = target.read_text(encoding="utf-8-sig", errors="replace").splitlines()[int(m["line"]) - 1]
        except (OSError, IndexError):
            gaps.append(f"{m['path']}:{m['line']} isn't there")
            continue
        if m["code"].strip() not in line:
            gaps.append(f"{m['path']}:{m['line']} doesn't hold `{m['code'].strip()}`")
    for m in NEW.finditer(body):
        what = (m["what"] or m["bare"] or "").strip()
        why = (m["why"] or "").strip()
        words = re.findall(r"[A-Za-z_][A-Za-z0-9_]*", what)
        name = words[-1] if words else what
        if "because" not in why.lower():
            gaps.append(f"{name} is new with no reason: say `because` why nothing in the repo fits")
        where = defined_at(repo, name) if name else None
        if where:
            gaps.append(f"{name} already exists at {where}: build on it, or say why it can't be used")
    return gaps


def check_api(text: str) -> list[str]:
    body = section(text, "API for its callers")
    outside = text if body is None else text.replace(body, "")
    if body is None:
        return ["the plan adds or changes an endpoint and has no `## API for its callers`: an example, the errors, "
                "what old app versions see"] if ENDPOINT.search(outside) else []
    gaps = []
    if "```" not in body:
        gaps.append("the API section has no example request or response")
    if not re.search(r"^-?\s*errors?\s*:", body, re.M | re.I):
        gaps.append("the API section has no errors: which status for which case")
    if not re.search(r"old app versions?", body, re.I):
        gaps.append("the API section doesn't say what old app versions see")
    return gaps


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("plan")
    ap.add_argument("--story")
    ap.add_argument("--repo", default=".")
    args = ap.parse_args(argv)
    text = Path(args.plan).read_text(encoding="utf-8-sig").replace("\r\n", "\n")
    notes = []
    gaps = []
    if args.story:
        gaps += check_scope(text, Path(args.story).read_text(encoding="utf-8-sig").replace("\r\n", "\n"))
    else:
        notes.append("no story: scope not checked (pass --story)")
    gaps += check_design(text, Path(args.repo))
    gaps += check_api(text)
    if gaps:
        print(f"Plan check: NOT READY ({len(gaps)})")
        for i, gap in enumerate(gaps, 1):
            print(f"{i}. {gap}")
    else:
        print("Plan check: READY")
    for note in notes:
        print(note)
    return 1 if gaps else 0


if __name__ == "__main__":
    sys.exit(main())
