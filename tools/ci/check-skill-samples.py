#!/usr/bin/env python3
"""Every code block marked <!-- sample: path --> in a skill must appear, line for line, in that file.

The file is compiled and run in CI (tests/SkillSamples.Tests), so the marked block in the skill is proven
code. This check stops the two from drifting: edit the code, and the skill must be updated too.
"""
import pathlib
import re
import sys

root = pathlib.Path(__file__).resolve().parents[2]
pattern = re.compile(r"<!-- sample: (?P<path>[^ ]+) -->\s*\n```[a-z]*\n(?P<code>.*?)\n```", re.S)
problems, checked = [], 0
# Installed skills and the per-project ones a repo copies in: both show tested code.
skills = sorted([*(root / ".claude" / "skills").glob("*/SKILL.md"), *(root / "project-skills").glob("*/SKILL.md")])
for skill in skills:
    for m in pattern.finditer(skill.read_text(encoding="utf-8")):
        checked += 1
        source = root / m["path"]
        if not source.exists():
            problems.append(f"{skill.parent.name}: sample file missing: {m['path']}")
            continue
        block = [l.rstrip() for l in m["code"].splitlines()]
        lines = [l.rstrip() for l in source.read_text(encoding="utf-8").splitlines()]
        if not any(lines[i:i + len(block)] == block for i in range(len(lines) - len(block) + 1)):
            problems.append(f"{skill.parent.name}: block marked as {m['path']} no longer matches that file")
# Story and production tests: the folders a skill's samples live in must also hold both kinds.
required = {l.strip() for l in (root / "tests" / "story-and-production.txt").read_text(encoding="utf-8").splitlines()
            if l.strip() and not l.startswith("#")}
pending = []
for skill in skills:
    paths = [m["path"] for m in pattern.finditer(skill.read_text(encoding="utf-8"))]
    if not paths:
        continue
    folders = {(root / p).parent for p in paths}
    code = "".join(f.read_text(encoding="utf-8") for d in folders if d.is_dir() for f in d.glob("*.cs"))
    missing = [kind for kind in ("Story", "Production")
               if f"ProductionConditions.{kind})" not in code and f'"Kind", "{kind}")' not in code]
    name = skill.parent.name
    if missing and name in required:
        problems.append(f"{name}: no {' and no '.join(k.lower() for k in missing)} test")
    elif missing:
        pending.append(name)
for name in sorted(required - {s.parent.name for s in skills}):
    problems.append(f"tests/story-and-production.txt lists {name}, which isn't a skill")
if pending:
    print(f"::notice title=Story and production tests::not yet for {len(pending)} skill(s): {', '.join(pending)}")

for p in problems:
    print(f"✗ {p}")
print(f"{checked} marked sample block(s) checked, {len(problems)} out of sync")
sys.exit(1 if problems else 0)
