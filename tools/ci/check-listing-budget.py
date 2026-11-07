"""Does the skill listing still fit?

Claude Code puts every skill's and command's name and description into one listing at the start of a
session. The listing has a budget of 1% of the model's context window, shared with Claude Code's own
skills and any plugin's, and when it overflows Claude Code drops the descriptions of the least-used ones:
Claude then sees only a name and can't match a request to it. On a 1M window in November 2026, about 20
of ours had lost theirs. So the descriptions here are kept short, and this fails when they grow back.

A command with `disable-model-invocation: true` is left out of the listing, so it isn't counted.

    python tools/ci/check-listing-budget.py [--claude-dir .claude ...] [--max-chars N] [--max-each N]

A team layer counts its listing with the core's, against a budget for both.
"""
import argparse
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
# About 3,000 tokens for the core: with the built-in skills (about 2,500), a team layer and a plugin or
# two it stays under the 10,000 a 1M window allows, with room to spare.
MAX_CHARS = 12000
MAX_EACH = 180


def frontmatter(path):
    text = path.read_text(encoding="utf-8")
    if not text.startswith("---\n"):
        return {}
    head = text[4:text.index("\n---", 3)]
    fields = {}
    for line in head.splitlines():
        match = re.match(r"^([a-z-]+):\s*(.*)$", line)
        if match:
            fields[match.group(1)] = match.group(2).strip().strip('"').strip("'")
    return fields


def listing(claude_dirs):
    """(kind, name, description) for every entry Claude Code puts in the listing."""
    entries = []
    for base in claude_dirs:
        for path in sorted(base.glob("skills/*/SKILL.md")):
            entries.append(("skill", path.parent.name, frontmatter(path).get("description", "")))
        for path in sorted(base.glob("commands/*.md")):
            fields = frontmatter(path)
            if fields.get("disable-model-invocation") != "true":
                entries.append(("command", path.stem, fields.get("description", "")))
    return entries


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--claude-dir", action="append", type=pathlib.Path)
    parser.add_argument("--max-chars", type=int, default=MAX_CHARS)
    parser.add_argument("--max-each", type=int, default=MAX_EACH)
    args = parser.parse_args(argv)
    entries = listing(args.claude_dir or [ROOT / ".claude"])
    total = sum(len(name) + len(desc) for _, name, desc in entries)
    long = [(name, len(desc)) for _, name, desc in entries if len(desc) > args.max_each]
    print(f"skill listing: {len(entries)} entries, {total} characters (budget {args.max_chars}), "
          f"longest description {max((len(d) for _, _, d in entries), default=0)} (limit {args.max_each})")
    for name, size in long:
        print(f"  {name}: description is {size} characters; keep it under {args.max_each}, what it does and when, keywords first")
    if total > args.max_chars:
        print(f"  the listing is over budget by {total - args.max_chars} characters: shorten descriptions, or hide a "
              f"command only the developer runs with disable-model-invocation: true")
    return 1 if long or total > args.max_chars else 0


if __name__ == "__main__":
    sys.exit(main())
