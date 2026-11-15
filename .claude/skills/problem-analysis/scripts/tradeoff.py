#!/usr/bin/env python3
"""Scores a weighted trade-off table and says how close the decision is.

    python tradeoff.py decision.md [--json]

decision.md holds one Markdown table: a `Criterion` column, a `Weight` column (1-10), then one column per
option with a score from 1 (worst) to 5 (best) for each criterion. Anything after the table (reasons) is
ignored. The first table in the file is the one read.

It prints each option's weighted total, the winner, and how robust the win is:
- for each criterion, how far its weight would have to move (within 0-10) before another option wins;
- whether any single score moved by one point changes the winner.
The decision is `close` when moving one weight by 2 or less, or one score by one point, changes it: then
the table can't settle it alone, and the fact that would settle it belongs in the ADR. Otherwise `clear`.

Exit 1 (each problem listed) when the table is missing a weight or a score, or one is out of range.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

CLOSE_WEIGHT = 2
WEIGHTS = range(0, 11)
SCORES = range(1, 6)


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


def parse(rows: list[list[str]]) -> tuple[dict[str, float], dict[str, dict[str, float]], list[str]]:
    problems: list[str] = []
    if len(rows) < 2:
        return {}, {}, ["no table with a header and at least one criterion"]
    header = rows[0]
    if len(header) < 4 or header[1].lower() != "weight":
        return {}, {}, ["the header must be: Criterion | Weight | Option A | Option B ..."]
    options = header[2:]
    weights: dict[str, float] = {}
    scores: dict[str, dict[str, float]] = {o: {} for o in options}
    for row in rows[1:]:
        name = row[0]
        cells = row + [""] * (len(header) - len(row))
        try:
            w = float(cells[1])
            if w not in WEIGHTS:
                problems.append(f"{name}: weight {cells[1]} isn't a whole number from 0 to 10")
            weights[name] = w
        except ValueError:
            problems.append(f"{name}: no weight")
        for o, raw in zip(options, cells[2:]):
            try:
                s = float(raw)
                if s not in SCORES:
                    problems.append(f"{name}, {o}: score {raw} isn't a whole number from 1 to 5")
                scores[o][name] = s
            except ValueError:
                problems.append(f"{name}, {o}: no score")
    if not any(weights.values()):
        problems.append("every weight is 0")
    return weights, scores, problems


def totals(weights: dict[str, float], scores: dict[str, dict[str, float]]) -> dict[str, float]:
    return {o: sum(weights[c] * s.get(c, 0) for c in weights) for o, s in scores.items()}


def winner(t: dict[str, float]) -> str | None:
    best = max(t.values())
    leaders = [o for o, v in t.items() if v == best]
    return leaders[0] if len(leaders) == 1 else None


def weight_moves(weights, scores, win) -> list[dict]:
    """For each criterion, the smallest move of its weight, within 0-10, that makes another option win or tie."""
    out = []
    for c, w in weights.items():
        best = None
        for new in WEIGHTS:
            if new == w:
                continue
            t = totals({**weights, c: new}, scores)
            if winner(t) != win and (best is None or abs(new - w) < abs(best[0] - w)):
                best = (new, max(t, key=t.get) if winner(t) else "a tie")
        out.append({"criterion": c, "weight": w, "flips_at": best[0] if best else None, "to": best[1] if best else None})
    return out


def score_flips(weights, scores, win) -> list[dict]:
    flips = []
    for o, s in scores.items():
        for c, v in s.items():
            for nv in (v - 1, v + 1):
                if nv in SCORES:
                    t = totals(weights, {**scores, o: {**s, c: nv}})
                    if winner(t) != win:
                        flips.append({"option": o, "criterion": c, "from": v, "to": nv, "result": winner(t) or "a tie"})
    return flips


def analyse(path: Path) -> dict:
    weights, scores, problems = parse(read_table(path))
    if problems:
        return {"problems": problems}
    t = totals(weights, scores)
    win = winner(t)
    moves = weight_moves(weights, scores, win) if win else []
    flips = score_flips(weights, scores, win) if win else []
    close = win is None or bool(flips) or any(m["flips_at"] is not None and abs(m["flips_at"] - m["weight"]) <= CLOSE_WEIGHT for m in moves)
    top = sum(weights.values()) * max(SCORES)
    return {"totals": {o: round(v / top * 100, 1) for o, v in sorted(t.items(), key=lambda kv: -kv[1])}, "winner": win,
            "verdict": "close" if close else "clear", "weights": moves, "score_flips": flips, "problems": []}


def main(argv=None) -> int:
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("table", type=Path)
    p.add_argument("--json", action="store_true")
    a = p.parse_args(argv)
    r = analyse(a.table)
    if r["problems"]:
        print("The table can't be scored yet:")
        print("\n".join(f"- {x}" for x in r["problems"]))
        return 1
    if a.json:
        print(json.dumps(r, indent=2, ensure_ascii=False))
        return 0
    print(f"Winner: {r['winner'] or 'a tie'} ({r['verdict']})\n")
    print("| Option | Weighted score |\n|---|---|")
    for o, v in r["totals"].items():
        print(f"| {o} | {v}% |")
    print()
    for m in r["weights"]:
        if m["flips_at"] is None:
            print(f"- {m['criterion']} (weight {m['weight']:g}): no weight from 0 to 10 changes the winner")
        else:
            outcome = "it's a tie" if m["to"] == "a tie" else f"{m['to']} wins"
            print(f"- {m['criterion']} (weight {m['weight']:g}): at {m['flips_at']:g}, {outcome}")
    for f in r["score_flips"]:
        outcome = "it's a tie" if f["result"] == "a tie" else f"{f['result']} wins"
        print(f"- one point: {f['option']} on {f['criterion']} at {f['to']:g} instead of {f['from']:g}: {outcome}")
    if r["verdict"] == "close":
        print("\nThe table can't settle this alone: name in the ADR the fact that would.")
    return 0


if __name__ == "__main__":
    # A Windows console is cp1252: an Arabic criterion would stop the report.
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.exit(main())
