"""Grades one copy of the business-rules eval against key.json, from its Referrals/rules.md and nothing else.

    python3 grade_one.py <folder holding Referrals/> <name> <results folder>

Writes <results>/<name>/: K1.txt..K6.txt (pass = an R line names one of that rule's lines), D1.txt..D5.txt
(pass = no R line names that decoy's lines, or describes it without saying it isn't enforced), build.txt
(always pass: nothing is built), and summary.json (the R lines, and whether business-rules' own --check
accepts the file: reported, not graded, as the rules-only arm never sees the script).
"""
import json
import re
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parents[1]
SCRIPT = HERE.parents[2] / ".claude" / "skills" / "business-rules" / "scripts" / "rule_candidates.py"
RULE = re.compile(r"^\s*(?:[-*+]\s*)?\**\s*(R\d+)\s*\**\s*[:.)]\s*(.+)$")
REF = re.compile(r"([\w./\\-]+\.(?:cs|json|md)):(\d+)")
NEGATION = re.compile(r"\b(not|never|isn'?t|doesn'?t|unused|dead|no code|only a comment|only the readme)\b", re.I)

src, name, results = Path(sys.argv[1]), sys.argv[2], Path(sys.argv[3])
out = results / name
out.mkdir(parents=True, exist_ok=True)
key = {k: v for k, v in json.loads((HERE / "key.json").read_text(encoding="utf-8")).items() if k != "_"}
rules_md = src / "Referrals" / "rules.md"
rules = []
if rules_md.is_file():
    for raw in rules_md.read_text(encoding="utf-8-sig").splitlines():
        if (m := RULE.match(raw)):
            refs = [(Path(f.replace("\\", "/")).name, int(n)) for f, n in REF.findall(m.group(2))]
            rules.append({"id": m.group(1), "text": m.group(2), "refs": refs})


def names(rule, lines):
    return any(f in lines and n in lines[f] for f, n in rule["refs"])


verdicts = {}
for k, v in key.items():
    if k.startswith("K"):
        verdicts[k] = any(names(r, v["lines"]) for r in rules)
    else:
        words = re.compile(v["words"], re.I)
        claimed = [r["id"] for r in rules if names(r, v["lines"]) or (words.search(r["text"]) and not NEGATION.search(r["text"]))]
        verdicts[k] = not claimed
    (out / f"{k}.txt").write_text("pass\n" if verdicts[k] else "fail\n")
(out / "build.txt").write_text("pass\n")
accepted = None
if rules_md.is_file():
    run = subprocess.run([sys.executable, str(SCRIPT), str(HERE / "Referrals" / "src"), "--check", str(rules_md)],
                         capture_output=True, text=True, encoding="utf-8")
    accepted = run.returncode == 0
(out / "summary.json").write_text(json.dumps({"rules": rules, "check accepts it": accepted}, indent=2, ensure_ascii=False))
print(name, " ".join(f"{k}={'ok' if ok else '--'}" for k, ok in verdicts.items()))
