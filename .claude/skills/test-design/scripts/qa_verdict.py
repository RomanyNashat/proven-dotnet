#!/usr/bin/env python3
"""The ship verdict for a story's branch, from the evidence the other scripts produce.

    python qa_verdict.py --trace trace.json [--mutation mutation.json] [--accept accepted.txt] [--json]

trace.json is `trace_matrix.py ... --trx results.trx --json`; mutation.json is `mutation_report.py ...
--since REF --json`. accepted.txt lists survivors judged harmless, one per line: `File.cs:19 why`.

Edge cases (EC lines) and abuse cases (AB lines) count as criteria here. A criterion marked `(inferred)` counts too, and the verdict
line says how many are inferred, so a YES on assumptions reads as one.

YES (exit 0): every criterion is proven (each of its tests ran and passed), no test names a criterion the
story lacks, and every change the tests missed on the changed lines is accepted with a reason.
NO (exit 1): any of those fails; each reason is listed.
CAN'T SAY (exit 2): the criteria hold but the tests' strength wasn't measured (no mutation report), or the
trace ran without test results. A verdict needs evidence, so missing evidence is never a YES.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path


def accepted(path: Path | None) -> dict[str, str]:
    if path is None:
        return {}
    out = {}
    for line in path.read_text(encoding="utf-8-sig").splitlines():
        line = line.strip()
        if line and not line.startswith("#"):
            where, _, why = line.partition(" ")
            out[where.replace("\\", "/")] = why.strip()
    return out


def verdict(trace: dict, mutation: dict | None, accepts: dict[str, str]) -> dict:
    reasons, notes = [], []
    statuses = {r["criterion"]: r["status"] for r in trace["criteria"]}
    if any(s == "covered" for s in statuses.values()):
        notes.append("the trace ran without test results (--trx), so no criterion is proven yet")
    for ac, status in statuses.items():
        if status not in ("proven", "covered"):
            reasons.append(f"{ac} is {status}")
    for u in trace.get("unknown", []):
        reasons.append(f"tests name {u['criterion']}, which the story doesn't have")
    for o in trace.get("orphans", []):
        reasons.append(f"{o} doesn't start with a criterion the story has")
    for u in trace.get("unnamed", []):
        reasons.append(f"{u} doesn't say who abuses it")
    if mutation is None:
        notes.append("the tests' strength wasn't measured (no mutation report)")
    else:
        for m in mutation["missed"]:
            where = f"{m['file']}:{m['line']}"
            if not any(where.endswith(a) or a.endswith(where) for a in accepts):
                reasons.append(f"{where}: `{m['original']}` became `{m['replacement']}` and no test noticed ({m['status']})")
    if reasons:
        result = "NO"
    elif notes:
        result = "CAN'T SAY"
    else:
        result = "YES"
    return {"verdict": result, "reasons": reasons, "notes": notes,
            "proven": sum(s == "proven" for s in statuses.values()), "criteria": len(statuses),
            "inferred": sum(1 for r in trace["criteria"] if r.get("inferred")),
            "abuse": sum(1 for r in trace["criteria"] if r["criterion"].startswith("AB")),
            "mutation_score": mutation["score"] if mutation else None}


def main(argv=None) -> int:
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("--trace", type=Path, required=True)
    p.add_argument("--mutation", type=Path)
    p.add_argument("--accept", type=Path)
    p.add_argument("--json", action="store_true")
    a = p.parse_args(argv)
    trace = json.loads(a.trace.read_text(encoding="utf-8-sig"))
    mutation = json.loads(a.mutation.read_text(encoding="utf-8-sig")) if a.mutation else None
    r = verdict(trace, mutation, accepted(a.accept))
    if a.json:
        print(json.dumps(r, indent=2))
    else:
        score = "not measured" if r["mutation_score"] is None else f"{r['mutation_score']}% on the changed lines"
        inferred = f"; {r['inferred']} inferred, to confirm" if r["inferred"] else ""
        inferred += f"; {r['abuse']} abuse cases" if r["abuse"] else ""
        print(f"Ship: {r['verdict']}  (criteria proven {r['proven']}/{r['criteria']}{inferred}; mutation {score})")
        for line in r["reasons"]:
            print(f"- {line}")
        for line in r["notes"]:
            print(f"- note: {line}")
    return {"YES": 0, "NO": 1, "CAN'T SAY": 2}[r["verdict"]]


if __name__ == "__main__":
    # A Windows console is cp1252: an Arabic criterion or line of code would stop the script mid-report.
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.exit(main())
