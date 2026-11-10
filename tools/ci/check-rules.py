"""Do the always-loaded instructions still hold every rule, and do they still fit?

core.md and rules/ load into every session, so they are kept to what Claude needs everywhere; the rest
lives in skills, which load when the work calls for them. tests/rules/inventory.tsv names every rule the
always-loaded files held when they were cut, and the file that holds it now: this fails when one goes
missing, and when the always-loaded files grow past their budget.

    python tools/ci/check-rules.py [--claude-dir .claude] [--inventory tests/rules/inventory.tsv] [--max-chars N]
"""
import argparse
import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
# 41,800 characters before the cut (2026-11-10), about 28,800 after: room for a few new rules, not for
# reference material creeping back.
MAX_CHARS = 30000


def always_loaded(claude_dir):
    files = [claude_dir / "core.md", *sorted((claude_dir / "rules").glob("**/*.md"))]
    return [f for f in files if f.is_file()]


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--claude-dir", type=pathlib.Path, default=ROOT / ".claude")
    parser.add_argument("--inventory", type=pathlib.Path, default=ROOT / "tests" / "rules" / "inventory.tsv")
    parser.add_argument("--max-chars", type=int, default=MAX_CHARS)
    args = parser.parse_args(argv)
    problems = []
    for number, line in enumerate(args.inventory.read_text(encoding="utf-8").splitlines(), 1):
        if not line.strip() or line.startswith("#"):
            continue
        path, _, text = line.partition("\t")
        target = args.claude_dir / path
        if not target.is_file():
            problems.append(f"inventory line {number}: {path} doesn't exist")
        elif text not in target.read_text(encoding="utf-8"):
            problems.append(f"inventory line {number}: {path} no longer says {text!r}")
    total = sum(len(f.read_text(encoding="utf-8")) for f in always_loaded(args.claude_dir))
    print(f"always-loaded instructions: {total} characters (budget {args.max_chars})")
    if total > args.max_chars:
        problems.append(f"core.md and rules/ are {total - args.max_chars} characters over budget: move reference "
                        "and procedure into the skill that does the work, and leave a one-line pointer")
    for p in problems:
        print(f"  {p}")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
