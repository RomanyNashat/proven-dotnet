#!/usr/bin/env python3
"""Traces a story's acceptance criteria to the tests that prove them.

    python trace_matrix.py <story.md> <test folder> [--trx results.trx] [--require-all] [--json]
                           [--prefix AC] [--trait AC]

The story lists its criteria as `AC1: ...` lines (a list item, bold or plain). Tests name the criteria
they prove with xUnit traits, on the method or on the class:

    [Theory, Trait("AC", "AC2")]
    public void Decide_GuardianBooksForAChild_IsAllowed(...)

Without --trx, a criterion is `covered` when at least one test names it. With --trx (from
`dotnet test --logger trx`), it is `proven` when every result of every such test passed, `failing` when
one failed, and `not run` when none of its tests are in the results. Tests that name a criterion the
story doesn't have are listed too: a typo there would hide a gap.

--require-all exits 1 unless every criterion is covered (proven, with --trx).

--prefix and --trait trace other numbered lines the same way: `business-rules` traces `R1: ...` rules to
`[Trait("Rule", "R1")]` with `--prefix R --trait Rule`.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

def patterns(prefix: str = "AC", trait: str = "AC") -> tuple[re.Pattern, re.Pattern]:
    p, t = re.escape(prefix), re.escape(trait)
    return (re.compile(rf"^\s*(?:[-*+]|\d+[.)])?\s*\**\s*({p}\d+)\s*\**\s*[:.)–—-]\s*\**\s*(.+?)\s*$"),
            re.compile(rf'Trait\(\s*"{t}"\s*,\s*"({p}\d+)"\s*\)'))


CRITERION, TRAIT = patterns()
NAMESPACE = re.compile(r"^\s*namespace\s+([\w.]+)")
CLASS = re.compile(r"\b(?:class|record)\s+(\w+)")
METHOD = re.compile(r"\b(?:public|internal|private|protected)\s+(?:static\s+)?(?:async\s+)?[\w<>\[\],.? ]+?\s+(\w+)\s*\(")


def read_text(path: Path) -> str:
    return path.read_text(encoding="utf-8-sig", errors="replace")


def criteria(story: Path) -> dict[str, str]:
    found = {}
    for line in read_text(story).splitlines():
        m = CRITERION.match(line)
        if m:
            found.setdefault(m.group(1), m.group(2))
    return found


def closing_bracket(line: str) -> int:
    """Index of the `]` that closes the attribute list at the start of the line (or the line's end)."""
    depth, quoted = 0, False
    for i, ch in enumerate(line):
        if ch == '"':
            quoted = not quoted
        elif not quoted and ch == "[":
            depth += 1
        elif not quoted and ch == "]":
            depth -= 1
            if depth == 0:
                return i
    return len(line) - 1


def traced_tests(folder: Path) -> dict[str, set[str]]:
    """Maps each test, as Namespace.Class.Method, to the criteria it names."""
    tests: dict[str, set[str]] = {}
    for path in sorted(folder.rglob("*.cs")):
        if any(part in ("bin", "obj") for part in path.parts):
            continue
        namespace, cls, class_acs, pending = "", "", set(), set()
        for raw in read_text(path).splitlines():
            line = raw.strip()
            if m := NAMESPACE.match(line):
                namespace = m.group(1).rstrip(";")
            if not line or line.startswith("//"):
                continue
            while line.startswith("["):
                end = closing_bracket(line)
                pending |= set(TRAIT.findall(line[:end + 1]))
                line = line[end + 1:].strip()
            if not line:
                continue
            if (c := CLASS.search(line)) and not set("(=.") & set(line[:c.start()]):
                cls, class_acs, pending = c.group(1), pending, set()
                continue
            if m := METHOD.search(line):
                named = pending | class_acs
                if named:
                    tests.setdefault(f"{namespace}.{cls}.{m.group(1)}", set()).update(named)
            pending = set()
    return tests


def outcomes(trx: Path) -> dict[str, list[str]]:
    root = ET.parse(trx).getroot()
    ns = {"t": root.tag.split("}")[0].strip("{")} if root.tag.startswith("{") else {"t": ""}
    q = (lambda tag: f".//t:{tag}") if ns["t"] else (lambda tag: f".//{tag}")
    method_of = {}
    for test in root.findall(q("UnitTest"), ns):
        m = test.find("t:TestMethod" if ns["t"] else "TestMethod", ns)
        if m is not None:
            method_of[test.get("id")] = f"{m.get('className')}.{m.get('name')}"
    results: dict[str, list[str]] = {}
    for r in root.findall(q("UnitTestResult"), ns):
        name = method_of.get(r.get("testId"))
        if name:
            results.setdefault(name, []).append(r.get("outcome", ""))
    return results


def matrix(story: Path, folder: Path, trx: Path | None) -> dict:
    acs = criteria(story)
    tests = traced_tests(folder)
    ran = outcomes(trx) if trx else None
    rows = []
    for ac, text in acs.items():
        names = sorted(t for t, named in tests.items() if ac in named)
        if not names:
            status = "uncovered"
        elif ran is None:
            status = "covered"
        else:
            seen = [o for n in names for o in ran.get(n, [])]
            status = "not run" if not seen else "failing" if any(o != "Passed" for o in seen) else "proven"
        row = {"criterion": ac, "text": text, "status": status, "tests": names}
        if ran is not None:
            row["not_run"] = [n for n in names if n not in ran]
        rows.append(row)
    unknown = sorted({ac for named in tests.values() for ac in named} - set(acs))
    return {"criteria": rows, "unknown": [{"criterion": ac, "tests": sorted(t for t, n in tests.items() if ac in n)} for ac in unknown]}


def main(argv=None) -> int:
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("story", type=Path)
    p.add_argument("tests", type=Path)
    p.add_argument("--trx", type=Path)
    p.add_argument("--require-all", action="store_true")
    p.add_argument("--json", action="store_true")
    p.add_argument("--prefix", default="AC", help="the numbered lines to trace (AC, or R for rules)")
    p.add_argument("--trait", default=None, help="the trait the tests use (default: the prefix)")
    a = p.parse_args(argv)
    global CRITERION, TRAIT
    CRITERION, TRAIT = patterns(a.prefix, a.trait or a.prefix)
    result = matrix(a.story, a.tests, a.trx)
    if not result["criteria"]:
        print(f"no {a.prefix}1: ... lines found in {a.story}", file=sys.stderr)
        return 2
    if a.json:
        print(json.dumps(result, indent=2))
    else:
        print("| Criterion | Status | Tests |\n|---|---|---|")
        for row in result["criteria"]:
            short = [t.rsplit(".", 1)[-1] for t in row["tests"]]
            print(f"| {row['criterion']}: {row['text']} | {row['status']} | {', '.join(short) or '-'} |")
        for u in result["unknown"]:
            print(f"\nTests name {u['criterion']}, which the story doesn't have: {', '.join(u['tests'])}")
    good = {"proven"} if a.trx else {"covered", "proven"}
    if a.require_all and any(row["status"] not in good for row in result["criteria"]):
        return 1
    return 0


if __name__ == "__main__":
    # A Windows console is cp1252: an Arabic criterion or line of code would stop the script mid-report.
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.exit(main())
