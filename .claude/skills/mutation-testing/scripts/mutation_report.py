#!/usr/bin/env python3
"""Reads Stryker.NET's JSON report and says which changes to the code the tests didn't notice.

    python mutation_report.py <mutation-report.json> [--since REF] [--repo DIR] [--min-score N] [--json]

A mutant is the code with one small change (`>=` made `>`, a condition negated, a block removed). It is
detected when a test fails on it (Killed, or Timeout), and missed when the tests still pass (Survived) or
no test runs that line (NoCoverage). The score is detected / (detected + missed); Ignored, CompileError
and the like don't count, as in Stryker's own score.

--since REF keeps only the mutants on lines changed since REF (`git diff -U0 REF`), so a pull request is
judged on the code it touched, not on the whole project. --min-score N exits 1 when the score is below N
(a percentage), or when a mutant was missed and N is 100. With no mutants left to judge, it says so and
exits 0.
"""
from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
from pathlib import Path

DETECTED = {"Killed", "Timeout"}
MISSED = {"Survived", "NoCoverage"}
HUNK = re.compile(r"^@@ -\d+(?:,\d+)? \+(\d+)(?:,(\d+))? @@")


def changed_lines(repo: Path, since: str) -> dict[str, set[int]]:
    """Lines added or changed since `since`, by path relative to the repo's root."""
    top = subprocess.run(["git", "-C", str(repo), "rev-parse", "--show-toplevel"], capture_output=True, text=True, check=True).stdout.strip()
    diff = subprocess.run(["git", "-C", top, "diff", "-U0", "--no-color", "--no-ext-diff", since, "--", "*.cs"],
                          capture_output=True, text=True, check=True).stdout
    lines: dict[str, set[int]] = {}
    current = None
    for line in diff.splitlines():
        if line.startswith("+++ "):
            current = None if line[4:] == "/dev/null" else line[6:]
            continue
        m = HUNK.match(line)
        if m and current:
            start, count = int(m.group(1)), int(m.group(2) or 1)
            lines.setdefault(current, set()).update(range(start, start + count))
    return {str(Path(top, p).resolve()).replace("\\", "/"): s for p, s in lines.items()}


def snippet(source: str, location: dict) -> str:
    rows = source.splitlines()
    start, end = location["start"], location["end"]
    if start["line"] == end["line"]:
        return rows[start["line"] - 1][start["column"] - 1:end["column"] - 1]
    return rows[start["line"] - 1][start["column"] - 1:].strip() + " ..."


def shown(full: str, root: str) -> str:
    """The file as a reader finds it: from the mutated project's folder when it's inside it."""
    base = str(Path(root).resolve()).replace("\\", "/").rstrip("/") if root else ""
    return full[len(base) + 1:] if base and full.startswith(base + "/") else full


def read(report: Path, only: dict[str, set[int]] | None) -> dict:
    data = json.loads(report.read_text(encoding="utf-8-sig"))
    root = data.get("projectRoot") or ""
    mutants = []
    for path, f in data["files"].items():
        full = str(Path(root, path).resolve() if not Path(path).is_absolute() else Path(path).resolve()).replace("\\", "/")
        keep = only.get(full) if only is not None else None
        for m in f["mutants"]:
            line = m["location"]["start"]["line"]
            if only is not None and (keep is None or line not in keep):
                continue
            mutants.append({
                "file": shown(full, root), "line": line, "status": m["status"], "mutator": m.get("mutatorName", ""),
                "original": snippet(f.get("source", ""), m["location"]) if f.get("source") else "",
                "replacement": m.get("replacement", ""),
            })
    detected = sum(m["status"] in DETECTED for m in mutants)
    missed = [m for m in mutants if m["status"] in MISSED]
    judged = detected + len(missed)
    return {"score": round(100 * detected / judged, 2) if judged else None, "detected": detected,
            "missed": missed, "judged": judged, "all": len(mutants)}


def main(argv=None) -> int:
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("report", type=Path)
    p.add_argument("--since")
    p.add_argument("--repo", type=Path, default=Path("."))
    p.add_argument("--min-score", type=float)
    p.add_argument("--json", action="store_true")
    a = p.parse_args(argv)
    only = changed_lines(a.repo, a.since) if a.since else None
    r = read(a.report, only)
    if a.json:
        print(json.dumps(r, indent=2))
    elif r["score"] is None:
        print("No mutants to judge" + (f" on the lines changed since {a.since}." if a.since else "."))
    else:
        scope = f" on the lines changed since {a.since}" if a.since else ""
        print(f"Mutation score{scope}: {r['score']:.2f}% ({r['detected']} of {r['judged']} changes detected)")
        if r["missed"]:
            print("\nChanges the tests didn't notice:\n\n| Where | Was | Became | Status |\n|---|---|---|---|")
            for m in sorted(r["missed"], key=lambda m: (m["file"], m["line"])):
                print(f"| {m['file']}:{m['line']} | `{m['original']}` | `{m['replacement']}` | {m['status']} |")
    if a.min_score is not None and r["score"] is not None and r["score"] < a.min_score:
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
