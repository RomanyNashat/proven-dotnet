"""Grades one /story-check production run: what the run left behind and its last message, not its wording.

    python grade.py <file|jira> <run folder>
"""
import json
import subprocess
import sys
from pathlib import Path

CASE, RUN = sys.argv[1], Path(sys.argv[2])
WORK = RUN / "work"
SCRIPT = Path(__file__).resolve().parents[3] / ".claude" / "skills" / "story-check" / "scripts" / "story_check.py"
result = {}
for line in (RUN / "transcript.jsonl").read_text(encoding="utf-8").splitlines():
    try:
        e = json.loads(line)
    except ValueError:
        continue
    if e.get("type") == "result":
        result = e
text = result.get("result") or ""
report = WORK / ".qa" / "story-check.json"
checks = {}
if CASE == "file":
    checks["the run ended"] = bool(result) and not result.get("is_error")
    checks["a report was written to .qa/story-check.json"] = report.is_file()
    if report.is_file():
        out = subprocess.run([sys.executable, str(SCRIPT), str(report), "--story", str(WORK / "story.md"), "--repo", str(WORK)],
                             capture_output=True, text=True, encoding="utf-8")
        checks["story_check.py accepts it"] = out.returncode == 0
        items = json.loads(report.read_text(encoding="utf-8-sig"))["items"]
        questions = [i["question"] for i in items if i.get("verdict") == "gap"]
        ruling = "NOT READY" if questions else "READY"
        checks[f"the reply gives the ruling ({ruling})"] = ruling in text
        checks["the reply has every question word for word"] = all(q in text for q in questions)
        print(out.stdout)
else:
    checks["the run ended"] = bool(result) and not result.get("is_error")
    checks["no report was made without a story"] = not report.exists()
    checks["it asks for the story's text"] = "paste" in text.lower()
    checks["it says no Jira tool is connected"] = "jira" in text.lower()
for name, ok in checks.items():
    print(f"{'PASS' if ok else 'FAIL'}  {name}")
print(f"cost ${result.get('total_cost_usd', 0):.2f}, {result.get('num_turns')} turns, {result.get('duration_ms', 0) / 1000:.0f}s")
sys.exit(0 if all(checks.values()) else 1)
