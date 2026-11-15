#!/usr/bin/env python3
"""Lists the C# members (methods, constructors, properties) a branch changed, so their callers can be
retested.

    python changed_members.py [--since REF] [--repo DIR] [--json]

For each line added or changed since REF (default origin/main) in a .cs file, the member that holds it is
the nearest member declaration at or above that line in the file as it is now. A change on the signature
line belongs to that member, not to the one before it (git's own hunk header gets that case wrong). Lines
outside any member (usings, a field, an attribute on a class) are listed under the type. Test projects
are left out: their changes are tests, not code to retest.
"""
from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
from pathlib import Path

HUNK = re.compile(r"^@@ -\d+(?:,\d+)? \+(\d+)(?:,(\d+))? @@")
MODIFIERS = r"(?:public|private|protected|internal|static|virtual|override|sealed|abstract|async|extern|unsafe|new|partial|readonly|required)"
MEMBER = re.compile(
    rf"^\s*(?:\[[^\]]*\]\s*)*(?:{MODIFIERS}\s+)+(?:[\w<>\[\],.?() ]+?\s+)?(?P<name>~?\w+)\s*(?:<[^>]*>)?\s*(?P<kind>\(|\{{|=>)")
TYPE = re.compile(r"^\s*(?:\[[^\]]*\]\s*)*(?:\w+\s+)*?(?:class|record|struct|interface|enum)\s+(?P<name>\w+)")
KEYWORDS = {"if", "for", "foreach", "while", "switch", "using", "lock", "return", "catch", "new", "nameof", "typeof"}


def changed(repo: Path, since: str) -> tuple[Path, dict[str, set[int]]]:
    top = Path(subprocess.run(["git", "-C", str(repo), "rev-parse", "--show-toplevel"], capture_output=True, text=True,
                              check=True).stdout.strip())
    diff = subprocess.run(["git", "-C", str(top), "diff", "-U0", "--no-color", "--no-ext-diff", since, "--", "*.cs"],
                          capture_output=True, text=True, check=True).stdout
    lines: dict[str, set[int]] = {}
    current = None
    for line in diff.splitlines():
        if line.startswith("+++ "):
            current = None if line[4:] == "/dev/null" else line[6:]
        elif (m := HUNK.match(line)) and current:
            start, count = int(m.group(1)), int(m.group(2) or 1)
            lines.setdefault(current, set()).update(range(start, start + count))
    return top, lines


def is_test_file(path: str) -> bool:
    parts = path.replace("\\", "/").split("/")
    return any(p.endswith((".Tests", ".Test", ".UnitTests", ".IntegrationTests")) or p.lower() in ("tests", "test") for p in parts[:-1])


def members(text: str, changed_lines: set[int]) -> list[dict]:
    rows = text.splitlines()
    found: dict[tuple[str, str], dict] = {}
    for n in sorted(changed_lines):
        owner, kind, signature = None, "type", ""
        for i in range(min(n, len(rows)) - 1, -1, -1):
            m = MEMBER.match(rows[i])
            if m and m.group("name") not in KEYWORDS:
                owner, kind, signature = m.group("name"), "member", rows[i].strip()
                break
            t = TYPE.match(rows[i])
            if t:
                owner, signature = t.group("name"), rows[i].strip()
                break
        if owner is None:
            continue
        key = (kind, owner)
        found.setdefault(key, {"member": owner, "kind": kind, "signature": signature, "lines": []})["lines"].append(n)
    return list(found.values())


def main(argv=None) -> int:
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("--since", default="origin/main")
    p.add_argument("--repo", type=Path, default=Path("."))
    p.add_argument("--json", action="store_true")
    a = p.parse_args(argv)
    top, lines = changed(a.repo, a.since)
    result = []
    for path, nums in sorted(lines.items()):
        if is_test_file(path) or not (top / path).is_file():
            continue
        for m in members((top / path).read_text(encoding="utf-8-sig", errors="replace"), nums):
            result.append({"file": path, **m})
    if a.json:
        print(json.dumps(result, indent=2))
    elif not result:
        print(f"No code (outside the tests) changed since {a.since}.")
    else:
        print(f"Changed since {a.since}:\n\n| File | Member | Lines |\n|---|---|---|")
        for r in result:
            print(f"| {r['file']} | `{r['signature']}` | {', '.join(map(str, r['lines']))} |")
    return 0


if __name__ == "__main__":
    sys.exit(main())
