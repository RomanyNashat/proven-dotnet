#!/usr/bin/env python3
"""Scores the hub eval's runs against the bar in README.md.

    python grade.py <runs folder>     (holds flat/ and hubs/, each with index.tsv and <n>.jsonl)

For each run: the first skill the model opened that isn't a hub (by the Skill tool, or by reading its
SKILL.md, the hub's hop), whether a hub came first, and the turns it took."""
import json
import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
HUBS = set(json.loads((HERE / "hubs.json").read_text(encoding="utf-8")))
CLAUDE = HERE.parents[2] / ".claude"
AGENTS = {p.stem: p.read_text(encoding="utf-8") for p in (CLAUDE / "agents").glob("*.md")}
# A command's text, with the text of each agent it hands the work to (/health-check -> health-analyst).
COMMANDS = {p.stem: p.read_text(encoding="utf-8") for p in (CLAUDE / "commands").glob("*.md")}
COMMANDS = {name: text + "".join(AGENTS[a] for a in AGENTS if re.search(rf"\b{re.escape(a)}\b", text))
            for name, text in COMMANDS.items()}


def accepted(want: str) -> set[str]:
    """The skill, or a command that runs it (`/coverage` runs test-coverage): both land the request there."""
    return {want} | {name for name, text in COMMANDS.items() if re.search(rf"\b{re.escape(want)}\b", text)}
SKILL_FILE = re.compile(r"skills/([\w-]+)/SKILL\.md$")


def opened(path: Path) -> tuple[list[str] | None, int]:
    """The skills opened in order, or None when the session never ran (a usage limit, an API error)."""
    order, turns = [], 0
    for line in path.read_text(encoding="utf-8", errors="replace").splitlines():
        try:
            event = json.loads(line)
        except ValueError:
            continue
        if event.get("type") == "result":
            turns = event.get("num_turns", 0)
            if event.get("terminal_reason") == "api_error" or event.get("api_error"):
                return None, 0
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
        right, hops, turns, rows, skipped = 0, 0, [], [], 0
        for line in index.read_text(encoding="utf-8").splitlines():
            n, want = line.split("\t")
            path = runs / arm / f"{n}.jsonl"
            order, t = opened(path) if path.is_file() else (None, 0)
            if order is None:
                skipped += 1
                rows.append(f"SKIP  {arm} {n:>2} want {want:<24} (the session didn't run)")
                continue
            hubs = HUBS if arm == "hubs" else set()
            member = next((s for s in order if s not in hubs), None)
            ok = member in accepted(want)
            right += ok
            hops += bool(order and order[0] in hubs)
            turns.append(t)
            rows.append(f"{'PASS' if ok else 'FAIL'}  {arm} {n:>2} want {want:<24} opened {' > '.join(order) or '-'}")
        summary[arm] = {"right": right, "of": len(rows) - skipped, "not_run": skipped, "hub_first": hops, "mean_turns": round(sum(turns) / max(1, len(turns)), 1)}
        print("\n".join(rows))
    print(json.dumps(summary, indent=1))
    return 0


if __name__ == "__main__":
    sys.exit(main())
