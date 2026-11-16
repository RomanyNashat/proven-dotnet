#!/usr/bin/env python3
"""Turns three-point estimates in hours into what a plan can promise.

    python pert.py plan.md [--json]

plan.md holds one Markdown table: `Task | Best | Likely | Worst`, hours, and optionally an `Unknowns`
column. The first table in the file is read. For each task it gives the expected hours, (best + 4 x likely
+ worst) / 6, and the spread, (worst - best) / 6. For the plan: the expected total, and the hours it comes
in under 80% and 90% of the time (the total's spread is the root of the summed squares, read as a normal
curve).

It also flags what to fix in the plan before promising anything:
- a task whose worst is more than 3 times its likely: it hides an unknown; spike it or split it;
- a task whose likely is over 16 hours: too big to estimate well; split it;
- the tasks that make up most of the plan's spread.

Exit 1, listing each problem, when an estimate is missing, isn't a number, or doesn't go best <= likely
<= worst.
"""
from __future__ import annotations

import argparse
import json
import math
import sys
from decimal import ROUND_HALF_UP, Decimal
from pathlib import Path

Z = {"80%": 0.8416, "90%": 1.2816}
WIDE = 3.0
TOO_BIG = 16.0


def half_up(x: float, places: int = 1) -> float:
    """5.25 to 5.3, as a person rounds: Python's round() gives 5.2, and the total would disagree with its rows."""
    return float(Decimal(repr(x)).quantize(Decimal(1).scaleb(-places), rounding=ROUND_HALF_UP))


def read_table(path: Path) -> list[list[str]]:
    rows, started = [], False
    for line in path.read_text(encoding="utf-8-sig").splitlines():
        s = line.strip()
        if s.startswith("|"):
            started = True
            cells = [c.strip() for c in s.strip("|").split("|")]
            if not all(set(c) <= set("-: ") for c in cells):
                rows.append(cells)
        elif started:
            break
    return rows


def hours(raw: str) -> float:
    raw = raw.lower().removesuffix("h").strip().replace("٫", ".")
    raw = raw.translate(str.maketrans("٠١٢٣٤٥٦٧٨٩", "0123456789"))
    return float(raw)


def parse(rows: list[list[str]]) -> tuple[list[dict], list[str]]:
    if len(rows) < 2:
        return [], ["no table with a header and at least one task"]
    header = [h.lower() for h in rows[0]]
    need = ["task", "best", "likely", "worst"]
    if header[:4] != need:
        return [], ["the header must start: Task | Best | Likely | Worst (hours), then Unknowns if any"]
    unknowns_at = header.index("unknowns") if "unknowns" in header else None
    tasks, problems = [], []
    for row in rows[1:]:
        row = row + [""] * (len(header) - len(row))
        name = row[0] or "(unnamed)"
        try:
            b, m, w = (hours(row[i]) for i in (1, 2, 3))
        except ValueError:
            problems.append(f"{name}: best, likely and worst must each be hours (a number)")
            continue
        if not 0 <= b <= m <= w:
            problems.append(f"{name}: needs best <= likely <= worst (got {b:g}, {m:g}, {w:g})")
            continue
        tasks.append({"task": name, "best": b, "likely": m, "worst": w,
                      "unknowns": row[unknowns_at].strip() if unknowns_at is not None else ""})
    return tasks, problems


def estimate(tasks: list[dict]) -> dict:
    for t in tasks:
        t["expected"] = half_up((t["best"] + 4 * t["likely"] + t["worst"]) / 6)
        t["spread"] = half_up((t["worst"] - t["best"]) / 6, 2)
        flags = []
        if t["likely"] > 0 and t["worst"] > WIDE * t["likely"]:
            flags.append("worst is over 3x likely: spike or split it")
        if t["likely"] > TOO_BIG:
            flags.append(f"likely is over {TOO_BIG:g}h: split it")
        if t["worst"] > t["likely"] and not t["unknowns"] and t["worst"] > 2 * t["likely"]:
            flags.append("a wide range with no unknown named")
        t["flags"] = flags
    total = sum((t["best"] + 4 * t["likely"] + t["worst"]) / 6 for t in tasks)
    variance = sum(((t["worst"] - t["best"]) / 6) ** 2 for t in tasks)
    sd = math.sqrt(variance)
    drivers = []
    if variance:
        share = sorted(tasks, key=lambda t: -(t["worst"] - t["best"]) ** 2)
        acc = 0.0
        for t in share:
            part = ((t["worst"] - t["best"]) / 6) ** 2 / variance
            drivers.append({"task": t["task"], "share": int(half_up(part * 100, 0))})
            acc += part
            if acc >= 0.7:
                break
    return {"tasks": tasks, "expected": half_up(total), "spread": half_up(sd),
            "by": {k: math.ceil(total + z * sd) for k, z in Z.items()}, "drivers": drivers}


def main(argv=None) -> int:
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("plan", type=Path)
    p.add_argument("--json", action="store_true")
    a = p.parse_args(argv)
    tasks, problems = parse(read_table(a.plan))
    if problems:
        print("The estimates can't be added up yet:")
        print("\n".join(f"- {x}" for x in problems))
        return 1
    r = estimate(tasks)
    if a.json:
        print(json.dumps(r, indent=2, ensure_ascii=False))
        return 0
    print("| Task | Best | Likely | Worst | Expected | Flags |\n|---|---|---|---|---|---|")
    for t in r["tasks"]:
        print(f"| {t['task']} | {t['best']:g} | {t['likely']:g} | {t['worst']:g} | {t['expected']:g} | {'; '.join(t['flags']) or '-'} |")
    print(f"\nExpected: {r['expected']:g} hours. Done within {r['by']['80%']} hours 80% of the time, "
          f"within {r['by']['90%']} hours 90% of the time.")
    if r["drivers"]:
        print("Most of the uncertainty: " + ", ".join(f"{d['task']} ({d['share']}%)" for d in r["drivers"]) + ".")
    return 0


if __name__ == "__main__":
    # A Windows console is cp1252: an Arabic task name would stop the report.
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.exit(main())
