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
for skill in sorted((root / ".claude" / "skills").glob("*/SKILL.md")):
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
for p in problems:
    print(f"✗ {p}")
print(f"{checked} marked sample block(s) checked, {len(problems)} out of sync")
sys.exit(1 if problems else 0)
