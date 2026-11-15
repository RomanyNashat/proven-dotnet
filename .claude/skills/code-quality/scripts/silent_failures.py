#!/usr/bin/env python3
"""Lists the places in C# code where a failure can go unnoticed, for a reviewer to judge each one.

    python silent_failures.py <code folder> [--diff change.diff] [--json]

A failure goes unnoticed when the code catches it and carries on as if nothing happened, or reads a
missing value as a real one. It lists:

- `swallowed`: a catch with an empty body;
- `fallback`: a catch that returns a value that looks like success (0, null, false, an empty list, `Ok()`);
- `logged and ignored`: a catch that only logs, then carries on;
- `skipped`: a catch that `continue`s a loop, so one item's failure drops it quietly;
- `default for missing`: `?? 0`, `?? false`, `?? ""` or `?? []` after a lookup, so "not found" reads as zero;
- `not awaited`: `_ = SomethingAsync(...)`, so its failure is never seen;
- `parse ignored`: a `TryParse` whose result is thrown away.

It doesn't list a catch that rethrows, wraps the exception in another, returns an error result (`Problem`,
a 4xx or 5xx, `NotFound`, `BadRequest`), or catches a cancellation. Each place is a candidate: some are
right (a cache miss falling back to the database), and the reviewer says which, with why. Test projects,
bin and obj are left out.

With --diff (git diff -U0 output), each place is marked `changed` (one of its lines is in the change),
`touched` (its file is) or left out.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

CATCH = re.compile(r"\bcatch\b\s*(\([^)]*\))?\s*(when\s*\((?:[^()]|\([^()]*\))*\))?")
CANCELLATION = re.compile(r"OperationCanceledException|TaskCanceledException")
RETHROWS = re.compile(r"\bthrow\b")
ERROR_RESULT = re.compile(r"\b(?:Problem|ValidationProblem|NotFound|BadRequest|Conflict|Unauthorized|Forbid|StatusCode\s*\(\s*(?:StatusCodes\.Status)?[45]\d\d"
                          r"|InternalServerError|ServiceUnavailable|UnprocessableEntity)\b|Result\.Fail|Error\.|\.Failure\(")
SUCCESS_VALUE = re.compile(r"\breturn\s+(?:null|default|false|true|0m?|0\.0m?|\"\"|string\.Empty|\[\]|Array\.Empty|Enumerable\.Empty|new\s*\(\s*\)"
                           r"|new\s+(?:List|Dictionary|HashSet)<[^>]*>\s*\(\s*\)|(?:TypedResults\.|Results\.)?Ok\s*\(|NoContent\s*\()")
LOGS = re.compile(r"\b(?:_?logger|_?log)\s*\.\s*Log\w*\s*\(|\bLog(?:Error|Warning|Information|Critical|Debug)\s*\(", re.I)
CONTINUE = re.compile(r"\bcontinue\s*;")
DEFAULT_FOR_MISSING = re.compile(r"(?:FirstOrDefault|SingleOrDefault|LastOrDefault|Find|GetValueOrDefault|TryGetValue|\]|\))\s*(?:\?\.\s*\w+\s*)*"
                                 r"\?\?\s*(?:0m?|0\.0m?|false|\"\"|string\.Empty|\[\]|new\s*\(\s*\))\s*[;,)]")
NOT_AWAITED = re.compile(r"^\s*_\s*=\s*[\w.]+Async\s*\(")
PARSE_IGNORED = re.compile(r"^\s*(?:_\s*=\s*)?[\w.]+\.TryParse\s*\(")
HUNK = re.compile(r"^@@ -\S+ \+(?P<start>\d+)(?:,(?P<count>\d+))? @@")

WHY = {
    "swallowed": "the failure disappears: nothing is logged, returned or retried",
    "fallback": "the caller gets a normal-looking answer for a failed call",
    "logged and ignored": "logged, then the code goes on as if it worked",
    "skipped": "one item's failure drops it from the batch without a trace the caller sees",
    "default for missing": "a value that isn't there is read as a real zero, false or empty",
    "not awaited": "the task runs unobserved: its failure is never seen",
    "parse ignored": "a value that didn't parse goes on as the type's default",
}


def is_test(path: Path, root: Path) -> bool:
    parts = path.relative_to(root).parts[:-1]
    return any(p in ("bin", "obj") or p.endswith((".Tests", ".Test", ".UnitTests", ".IntegrationTests")) for p in parts)


def strip_comments_and_strings(line: str) -> str:
    line = re.sub(r'@?"(?:[^"\\]|\\.)*"', '""', line)
    return line.split("//", 1)[0]


def block(lines: list[str], start: int, col: int) -> tuple[int, str] | None:
    """From the first `{` at or after (start, col): the line its block closes on, and the block's body."""
    depth, opened, body = 0, False, []
    for i in range(start, min(start + 400, len(lines))):
        text = strip_comments_and_strings(lines[i])
        seg = text[col:] if i == start else text
        current = []
        for ch in seg:
            if ch == "{":
                depth += 1
                if depth == 1 and not opened:
                    opened = True
                    continue
            elif ch == "}" and opened:
                depth -= 1
                if depth == 0:
                    body.append("".join(current))
                    return i, "\n".join(body)
            if opened:
                current.append(ch)
        if opened:
            body.append("".join(current))
        elif seg.strip() and i > start:
            return None
    return None


def scan_file(path: Path, root: Path) -> list[dict]:
    lines = path.read_text(encoding="utf-8-sig", errors="replace").splitlines()
    rel = path.relative_to(root).as_posix()
    found = []
    for n, raw in enumerate(lines):
        line = strip_comments_and_strings(raw)
        if not line.strip() or raw.strip().startswith(("//", "*", "/*")):
            continue
        if (m := CATCH.search(line)):
            header = m.group(0)
            got = block(lines, n, m.end())
            if got:
                end, body = got
                kind = None
                if CANCELLATION.search(header) or RETHROWS.search(body) or ERROR_RESULT.search(body):
                    kind = None
                elif not body.strip().strip(";").strip():
                    kind = "swallowed"
                elif SUCCESS_VALUE.search(body):
                    kind = "fallback"
                elif CONTINUE.search(body):
                    kind = "skipped"
                elif LOGS.search(body) and not re.search(r"\breturn\b", body):
                    kind = "logged and ignored"
                if kind:
                    found.append({"file": rel, "line": n + 1, "end": end + 1, "kind": kind, "code": raw.strip()})
            continue
        for kind, pattern in (("default for missing", DEFAULT_FOR_MISSING), ("not awaited", NOT_AWAITED), ("parse ignored", PARSE_IGNORED)):
            if pattern.search(line):
                found.append({"file": rel, "line": n + 1, "end": n + 1, "kind": kind, "code": raw.strip()})
                break
    return found


def scan(root: Path) -> list[dict]:
    out = []
    for path in sorted(root.rglob("*.cs")):
        if not is_test(path, root):
            out.extend(scan_file(path, root))
    return out


def read_diff(path: Path) -> str:
    data = path.read_bytes()
    if data[:2] in (b"\xff\xfe", b"\xfe\xff"):
        return data.decode("utf-16")
    return data.decode("utf-8-sig", errors="replace")


def changed_lines(diff_text: str) -> dict[str, set[int]]:
    changed, current = {}, None
    for line in diff_text.splitlines():
        if line.startswith("+++ "):
            target = line[4:].strip()
            current = None if target == "/dev/null" else re.sub(r"^b/", "", target)
            if current:
                changed.setdefault(current, set())
        elif (m := HUNK.match(line)) and current:
            start, count = int(m["start"]), int(m["count"] or 1)
            changed[current].update(range(start, start + count))
    return changed


def place(found: list[dict], changed: dict[str, set[int]]) -> list[dict]:
    for f in found:
        lines = next((v for k, v in changed.items() if k == f["file"] or k.endswith("/" + f["file"])), None)
        f["change"] = "outside" if lines is None else "changed" if lines & set(range(f["line"], f["end"] + 1)) else "touched"
    return [f for f in found if f["change"] != "outside"]


def main(argv=None) -> int:
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("code", type=Path)
    p.add_argument("--diff", type=Path)
    p.add_argument("--json", action="store_true")
    a = p.parse_args(argv)
    found = scan(a.code)
    total = len(found)
    if a.diff:
        found = place(found, changed_lines(read_diff(a.diff)))
    if a.json:
        print(json.dumps(found, indent=2, ensure_ascii=False))
        return 0
    scope = f" in the change, {total - len(found)} outside it (not listed)" if a.diff else ""
    print(f"## Silent-failure candidates: {len(found)}{scope}\n")
    if not found:
        print("None found.")
        return 0
    change = "| Change " if a.diff else ""
    print(f"| Kind {change}| Where | Code | Why look |\n|---{'|---' if a.diff else ''}|---|---|---|")
    for f in found:
        c = f"| {f['change']} " if a.diff else ""
        print(f"| {f['kind']} {c}| `{f['file']}:{f['line']}` | `{f['code'][:90]}` | {WHY[f['kind']]} |")
    return 0


if __name__ == "__main__":
    # A Windows console is cp1252: an Arabic log message in a catch would stop the list.
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.exit(main())
