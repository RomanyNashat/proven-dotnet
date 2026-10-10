#!/usr/bin/env python3
"""Rewrites every <!-- sample: path --> block in a skill from its tested file, keeping the block's start.

    python tools/ci/refresh-samples.py .claude/skills/<name>/SKILL.md

The block starts at the same first line it starts at now (so a block can show part of a file), and
runs to the end of the file. For a block that shows a middle part, edit by hand.
"""
import pathlib, re, sys

root = pathlib.Path(__file__).resolve().parents[2]
pattern = re.compile(r"(<!-- sample: (?P<path>[^ ]+) -->\s*\n```[a-z]*\n)(?P<code>.*?)(\n```)", re.S)
for skill in sys.argv[1:]:
    path = pathlib.Path(skill)
    text = path.read_text(encoding="utf-8")

    def refresh(m):
        source = (root / m["path"]).read_text(encoding="utf-8").rstrip()
        first = m["code"].splitlines()[0]
        start = source.find(first)
        if start < 0:
            raise SystemExit(f"{skill}: first line of the {m['path']} block isn't in the file any more: {first!r}")
        return m.group(1) + source[start:] + m.group(4)

    path.write_text(pattern.sub(refresh, text), encoding="utf-8")
