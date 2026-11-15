#!/usr/bin/env python3
"""Finds the lines in C# code where a business rule may live, and checks a rules file accounts for each.

    python rule_candidates.py <code folder> [--json]
    python rule_candidates.py <code folder> --check rules.md

A candidate is a line that decides something: a guard (`if (...)` that returns or throws), a thrown
exception, a validation (a FluentValidation `RuleFor`, or a DataAnnotations attribute such as
`[MaxLength(500)]`), a query filter (`.Where(...)`), a status change, a limit (a numeric constant or a
TimeSpan field), or a calculation (`Math.Round` and friends, or a choice between two values). Test projects, bin and
obj are left out, and so are comments.

--check reads rules.md: rules as `R1: ... (File.cs:25 `the code`)` lines, and a `## Not rules` section of
`File.cs:19 `the code`: why` lines. The code in backticks is how the line is found again once lines move:
it must be within 3 lines of the number, and the line that holds it is the one counted. It exits 1,
listing each problem, when a candidate is neither a rule nor a not-rule, a rule names no line, or a line it
names isn't there, holds no code, or doesn't hold its snippet. It never says a rule is right: the tests do
that (`trace_matrix.py --prefix R --trait Rule`).
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

GUARD = re.compile(r"^\s*(?:else\s+)?if\s*\(")
EXITS = re.compile(r"\b(return|throw)\b")
KINDS = [
    ("validation", re.compile(r"\bRuleFor\s*\(|\[(?:[\w(), ]*,\s*)?(?:Required|MaxLength|MinLength|StringLength|Range|RegularExpression|EmailAddress|Phone)\b")),
    ("query filter", re.compile(r"\.Where\s*\(")),
    ("throw", re.compile(r"\bthrow\s+new\s+\w*Exception\b")),
    ("state change", re.compile(r"\b\w*Status\s*=(?![=>])")),
    ("limit", re.compile(r"\b(?:const\s+(?:int|long|decimal|double)|static\s+readonly\s+(?:TimeSpan|int|decimal))\s+\w+\s*=")),
    ("calculation", re.compile(r"\bMath\.(?:Round|Floor|Ceiling|Min|Max)\s*\(|=\s*[^=;]+\?[^?:;]+:[^;]+;")),
]
RULE = re.compile(r"^\s*(?:[-*+]\s*)?\**\s*(R\d+)\s*\**\s*[:.)]\s*(.+)$")
REF = re.compile(r"\(([\w./\\-]+\.cs):(\d+)(?:\s+`([^`]+)`)?\)")
NOT_RULE = re.compile(r"^\s*[-*+]\s*([\w./\\-]+\.cs):(\d+)(?:\s+`([^`]+)`)?\s*:\s*(.+)$")
DRIFT = 3


def is_test(path: Path, root: Path) -> bool:
    parts = path.relative_to(root).parts[:-1]
    return any(p in ("bin", "obj") or p.endswith((".Tests", ".Test", ".UnitTests", ".IntegrationTests")) for p in parts) \
        or path.name.endswith(("Tests.cs", "Test.cs"))


def code_lines(path: Path) -> list[str]:
    return path.read_text(encoding="utf-8-sig", errors="replace").splitlines()


def is_comment(line: str) -> bool:
    s = line.strip()
    return not s or s.startswith(("//", "/*", "*", "///")) or s in ("{", "}", "};", "});", ")")


def candidates(root: Path) -> list[dict]:
    found = []
    for path in sorted(root.rglob("*.cs")):
        if is_test(path, root):
            continue
        lines = code_lines(path)
        for n, line in enumerate(lines, 1):
            if is_comment(line):
                continue
            kind = None
            if GUARD.match(line):
                after = line + " " + next((l for l in lines[n:n + 3] if l.strip() and l.strip() != "{"), "")
                if EXITS.search(after):
                    kind = "guard"
            if kind is None:
                kind = next((k for k, pattern in KINDS if pattern.search(line)), None)
            if kind:
                found.append({"file": path.relative_to(root).as_posix(), "line": n, "kind": kind, "text": line.strip()})
    for i, c in enumerate(found, 1):
        c["id"] = f"C{i}"
    return found


def same_file(named: str, actual: str) -> bool:
    named = named.replace("\\", "/")
    return actual == named or actual.endswith("/" + named) or named.endswith("/" + actual)


def locate(root: Path, who: str, f: str, n: int, snippet: str, problems: list[str]) -> tuple[str, int] | None:
    """The line a reference means: the one within DRIFT lines of n that holds its snippet, nearest first."""
    files = [p for p in root.rglob("*.cs") if same_file(f, p.relative_to(root).as_posix())]
    if not files:
        problems.append(f"{who}: {f} isn't in {root}")
        return None
    lines = code_lines(files[0])
    if not snippet:
        problems.append(f"{who}: {f}:{n} needs the code it means, in backticks: ({f}:{n} `the code`)")
        return None
    if n > len(lines):
        problems.append(f"{who}: {f}:{n} is past the end ({len(lines)} lines)")
        return None
    for d in sorted(range(-DRIFT, DRIFT + 1), key=abs):
        i = n + d
        if 1 <= i <= len(lines) and not is_comment(lines[i - 1]) and " ".join(snippet.split()) in " ".join(lines[i - 1].split()):
            return f, i
    problems.append(f"{who}: {f}:{n} doesn't hold `{snippet}`, there or within {DRIFT} lines"
                    + (" (that line holds no code)" if is_comment(lines[n - 1]) else ""))
    return None


def check(root: Path, rules_file: Path, found: list[dict]) -> list[str]:
    problems, accounted = [], []
    in_not_rules = False
    for raw in code_lines(rules_file):
        if raw.startswith("#"):
            in_not_rules = raw.strip("# ").lower().startswith("not rules")
            continue
        if in_not_rules and (m := NOT_RULE.match(raw)):
            if (at := locate(root, f"not a rule {m.group(1)}:{m.group(2)}", m.group(1), int(m.group(2)), m.group(3) or "", problems)):
                accounted.append(at)
        elif (m := RULE.match(raw)):
            refs = REF.findall(m.group(2))
            if not refs:
                problems.append(f"{m.group(1)} names no line of code: add (File.cs:line `the code`)")
            for f, n, snippet in refs:
                if (at := locate(root, m.group(1), f, int(n), snippet, problems)):
                    accounted.append(at)
    for c in found:
        if not any(same_file(f, c["file"]) and n == c["line"] for f, n in accounted):
            problems.append(f"{c['file']}:{c['line']} ({c['kind']}) `{c['text']}`: no rule names it, and it isn't under Not rules")
    return problems


def main(argv=None) -> int:
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("code", type=Path)
    p.add_argument("--check", type=Path, metavar="RULES_MD")
    p.add_argument("--json", action="store_true")
    a = p.parse_args(argv)
    found = candidates(a.code)
    if a.check:
        problems = check(a.code, a.check, found)
        if problems:
            print(f"{a.check} doesn't account for the code yet:")
            print("\n".join(f"- {x}" for x in problems))
            return 1
        print(f"{a.check} accounts for all {len(found)} candidate lines.")
        return 0
    if a.json:
        print(json.dumps(found, indent=2, ensure_ascii=False))
        return 0
    print("| Id | Where | Kind | Line |\n|---|---|---|---|")
    for c in found:
        print(f"| {c['id']} | {c['file']}:{c['line']} | {c['kind']} | `{c['text']}` |")
    return 0


if __name__ == "__main__":
    # A Windows console is cp1252: an Arabic string in a guard would stop the list.
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.exit(main())
