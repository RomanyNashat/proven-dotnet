"""Grades one copy of the story-check eval against key.json, from what the copy wrote and nothing else.

    python3 grade_one.py <folder holding Referrals/> <name> <results folder>

Writes <results>/<name>/: G1.txt..G4.txt (pass = that planted gap was asked), A1.txt..A7.txt (pass = that
hidden answer was not asked), and summary.json: the questions per story, the ones asked about cases the
key doesn't grade, and whether story-check's own script accepts each report (reported, not graded: the
rules-only arm never sees the script).
"""
import json
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parents[1]
SCRIPT = HERE.parents[2] / ".claude" / "skills" / "story-check" / "scripts" / "story_check.py"
src, name, results = Path(sys.argv[1]), sys.argv[2], Path(sys.argv[3])
out = results / name
out.mkdir(parents=True, exist_ok=True)
key = {k: v for k, v in json.loads((HERE / "key.json").read_text(encoding="utf-8")).items() if k != "_"}

reports, summary = {}, {"stories": {}}
for story in ("refer", "withdraw"):
    path = src / "Referrals" / ".qa" / f"{story}.json"
    try:
        items = json.loads(path.read_text(encoding="utf-8-sig"))["items"]
    except (OSError, ValueError, KeyError, TypeError):
        items = []
    reports[story] = {i.get("item"): i for i in items if isinstance(i, dict)}
    graded = {v["item"] for v in key.values() if v["story"] == story}
    gaps = [i for i in items if isinstance(i, dict) and i.get("verdict") == "gap"]
    accepted = None
    if path.is_file():
        run = subprocess.run([sys.executable, str(SCRIPT), str(path), "--story", str(HERE / "Referrals" / "stories" / f"{story}.md"),
                              "--repo", str(HERE / "Referrals")], capture_output=True, text=True, encoding="utf-8")
        accepted = run.returncode == 0
    summary["stories"][story] = {"report": path.is_file(), "questions": len(gaps),
                                 "ungraded questions": [g.get("item") for g in gaps if g.get("item") not in graded],
                                 "script accepts it": accepted}

for k, v in key.items():
    asked = reports[v["story"]].get(v["item"], {}).get("verdict") == "gap"
    ok = asked if k.startswith("G") else not asked
    (out / f"{k}.txt").write_text("pass\n" if ok else "fail\n")
# Nothing is built here; the shared table reads build.txt, and a report that is missing simply asks nothing.
(out / "build.txt").write_text("pass\n")
(out / "summary.json").write_text(json.dumps(summary, indent=2))
print(name, " ".join(f"{k}={'ok' if (out / f'{k}.txt').read_text().strip() == 'pass' else '--'}" for k in key))
