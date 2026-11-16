#!/usr/bin/env python3
"""Checks lessons files before their items are folded in, naming each item and line:

    python lessons_check.py ~/.claude/lessons/*.md

- every item (### L<n>.) has a Status: open, applied (vX.Y.Z) or dropped (<reason>);
- every correction, taught step and failure has a Form, the cheapest that lasts:
    test   - a test that fails if it happens again (`Harness change` names a test file);
    check  - a script, hook, setting or known-traps rule that refuses it (`Harness change` names it);
    rule   - a line in a rule file or a skill's Gotchas, with `Why not a test:` saying what no check can see;
    prose  - a section of a skill, with `Why not a test:` too; a procedure is prose by nature and needs none;
    none   - the code or the harness already shows it: `Shown by:` names where, and the item is dropped;
- no secrets, connection strings, tokens or internal hosts: the file travels to the harness repo.
Exit 0 when every item is ready, 1 with the list.
"""
from __future__ import annotations

import re
import sys
from pathlib import Path

FORMS = ("test", "check", "rule", "prose", "none")
ITEM = re.compile(r"^###\s+(L\d+)\.\s*(.*)$")
FIELD = re.compile(r"^\s*-\s+\*\*([^*]+?):?\*\*:?\s*(.*)$")
STATUS = re.compile(r"^(open|applied \(v\d+\.\d+\.\d+\)|dropped \(.+\))$")
TEST_FILE = re.compile(r"(^|[\s`/])tests?/|Tests?\.cs\b|\btest_\w+\.py\b|\.test\.(m?js|ts)\b", re.I)
CHECK_FILE = re.compile(r"\.py\b|\.sh\b|\.ps1\b|hooks?/|settings(\.local)?\.json|known-traps|check-[\w-]+|analy[sz]er|\.editorconfig", re.I)
WHERE = re.compile(r"[\w./-]+\.\w+(:\d+)?|[\w-]+/[\w./-]+")
SECRETS = [
    (re.compile(r"\b(password|pwd|secret|api[_-]?key|token)\s*[=:]\s*\S{4,}", re.I), "a secret's value"),
    (re.compile(r"\b(Server|Host|Data Source)\s*=\s*[^;\s]+;", re.I), "a connection string"),
    (re.compile(r"\bBearer\s+[A-Za-z0-9._-]{12,}|\beyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}"), "a token"),
    (re.compile(r"-----BEGIN [A-Z ]*PRIVATE KEY-----"), "a private key"),
    (re.compile(r"\bAKIA[0-9A-Z]{16}\b"), "a cloud access key"),
    (re.compile(r"https?://(?:[\w-]+\.)*[\w-]+\.(?:local|internal|corp|lan|intranet)\b|https?://(?:10|192\.168|172\.(?:1[6-9]|2\d|3[01]))\.\d+|https?://(?!localhost\b)[a-z0-9-]+(?=[:/\s]|$)", re.I),
     "an internal address"),
]


def items(text: str) -> list[dict]:
    out, current, section = [], None, ""
    for n, line in enumerate(text.splitlines(), start=1):
        if line.startswith("## "):
            section = line[3:].strip().lower()
        m = ITEM.match(line)
        if m:
            current = {"id": m.group(1), "title": m.group(2).strip(), "line": n, "section": section, "fields": {}}
            out.append(current)
            continue
        f = FIELD.match(line)
        if current and f:
            current["fields"][f.group(1).strip().lower()] = (f.group(2).strip(), n)
    return out


def check_file(path: Path) -> list[str]:
    text = path.read_text(encoding="utf-8-sig")
    problems = []
    say = lambda n, msg: problems.append(f"{path.name}:{n}: {msg}")
    for n, line in enumerate(text.splitlines(), start=1):
        for pattern, what in SECRETS:
            if pattern.search(line):
                say(n, f"{what}: take it out, the file travels to the harness repo")
    for it in items(text):
        f = it["fields"]
        title = it["title"] if len(it["title"]) <= 60 else it["title"][:60].rsplit(" ", 1)[0] + "…"
        label = f"{it['id']} ({title})"
        status = f.get("status", ("", it["line"]))[0]
        if not STATUS.match(status):
            say(it["line"], f"{label}: status '{status}' is open, applied (vX.Y.Z) or dropped (<reason>)")
        if "worked" in it["section"] or "question" in it["section"]:
            continue
        procedure = it["title"].lower().startswith("procedure")
        form, form_line = f.get("form", ("", it["line"]))
        form = form.split()[0].strip("`").lower() if form else ""
        change = f.get("harness change", ("", 0))[0]
        if procedure and not form:
            continue
        if form not in FORMS:
            say(form_line, f"{label}: no Form; the cheapest that lasts: {', '.join(FORMS)}")
            continue
        if form == "test" and not TEST_FILE.search(change):
            say(form_line, f"{label}: Form test, but the harness change names no test file")
        if form == "check" and not CHECK_FILE.search(change):
            say(form_line, f"{label}: Form check, but the harness change names no script, hook, setting or rule")
        if form in ("rule", "prose") and not procedure and not f.get("why not a test", ("",))[0]:
            say(form_line, f"{label}: Form {form}: say in 'Why not a test:' what no test or check can see")
        if form == "none":
            shown = f.get("shown by", ("",))[0]
            if not WHERE.search(shown):
                say(form_line, f"{label}: Form none: 'Shown by:' names the file (and line) that already shows it")
            if status.startswith("applied"):
                say(form_line, f"{label}: Form none writes nothing, so it can't be applied: mark it dropped (shown by ...)")
    return problems


def main() -> int:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    paths = [Path(p) for p in sys.argv[1:]]
    if not paths:
        print(__doc__)
        return 2
    problems = []
    count = 0
    for p in paths:
        if not p.is_file():
            problems.append(f"{p}: no such file")
            continue
        count += len(items(p.read_text(encoding="utf-8-sig")))
        problems += check_file(p)
    for line in problems:
        print(line)
    print(f"lessons check: {count} item(s) in {len(paths)} file(s), {len(problems)} problem(s)")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
