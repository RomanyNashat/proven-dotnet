#!/usr/bin/env python3
"""Scores the hub eval's runs against the bar in README.md.

    python grade.py <runs folder>     (holds flat/ and hubs/, each with index.tsv and <n>.jsonl)

For each run: the first skill the model opened that isn't a hub (by the Skill tool, or by reading its
SKILL.md, the hub's hop), whether a hub came first, and the turns it took."""
import json
import re
import sys
from pathlib import Path

HUBS = set(json.loads((Path(__file__).resolve().parent / "hubs.json").read_text(encoding="utf-8")))
SKILL_FILE = re.compile(r"skills/([\w-]+)/SKILL\.md$")


def opened(path: Path) -> tuple[list[str], int]:
    order, turns = [], 0
    for line in path.read_text(encoding="utf-8", errors="replace").splitlines():
        try:
            event = json.loads(line)
        except ValueError:
            continue
        if event.get("type") == "result":
            turns = event.get("num_turns", 0)
        if event.get("type") != "assistant":
            continue
        for block in event.get("message", {}).get("content", []):
            if block.get("type") != "tool_use":
                continue
            args = block.get("input", {})
            if block.get("name") == "Skill":
                order.append(str(args.get("skill", "")).split(":")[-1])
            elif block.get("name") == "Read":
                m = SKILL_FILE.search(str(args.get("file_path", "")).replace("\\", "/"))
                if m:
                    order.append(m.group(1))
    return order, turns


def main() -> int:
    runs = Path(sys.argv[1])
    summary = {}
    for arm in ("flat", "hubs"):
        index = runs / arm / "index.tsv"
        if not index.is_file():
            continue
        right, hops, turns, rows = 0, 0, [], []
        for line in index.read_text(encoding="utf-8").splitlines():
            n, want = line.split("\t")
            order, t = opened(runs / arm / f"{n}.jsonl")
            member = next((s for s in order if s not in HUBS), None)
            ok = member == want
            right += ok
            hops += bool(order and order[0] in HUBS)
            turns.append(t)
            rows.append(f"{'PASS' if ok else 'FAIL'}  {arm} {n:>2} want {want:<24} opened {' > '.join(order) or '-'}")
        summary[arm] = {"right": right, "of": len(rows), "hub_first": hops, "mean_turns": round(sum(turns) / max(1, len(turns)), 1)}
        print("\n".join(rows))
    print(json.dumps(summary, indent=1))
    return 0


if __name__ == "__main__":
    sys.exit(main())
