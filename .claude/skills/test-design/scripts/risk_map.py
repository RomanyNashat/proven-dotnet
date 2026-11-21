#!/usr/bin/env python3
"""Rank a repo's files by where the next bug is likely to be, from its git history.

A file that changes often, and whose changes are often fixes, is where a new change most needs its edge
cases. The score is plain so a person can check it: changes + 2 x fixes, ties broken by the most recent.

    python risk_map.py <repo> [--since "90 days ago"] [--ext .cs] [--with-tests] [--top 15] [--json]

A commit is a fix when its subject starts with fix: / fix(scope): / hotfix, or names a bug, a regression
or a fix. Deleted files are left out; renames count under the new name only. Read-only: it runs git log.
"""
import argparse
import json
import re
import subprocess
import sys
import time
from pathlib import Path

FIX = re.compile(r"^\s*(hot)?fix(\(.+?\))?!?:|\b(fix(es|ed)?|bug(fix)?|hotfix|regression)\b", re.IGNORECASE)
TEST_PATH = re.compile(r"(^|/)tests?(/|$)|Tests?\.cs$|\.Tests?([./]|$)", re.IGNORECASE)
SEPARATOR = "\x1e"


def is_fix(subject):
    return bool(FIX.search(subject))


def history(repo, since):
    # quotepath=off keeps an Arabic or accented file name as it is, instead of octal escapes
    out = subprocess.run(
        ["git", "-c", "core.quotepath=off", "-C", str(repo), "log", "--no-merges", "--no-renames",
         f"--since={since}", f"--format={SEPARATOR}%ct%x1f%s", "--name-only"],
        capture_output=True, text=True, encoding="utf-8", errors="replace")
    if out.returncode != 0:
        raise SystemExit(f"risk_map: git log failed in {repo}: {out.stderr.strip()}")
    for block in out.stdout.split(SEPARATOR)[1:]:
        head, _, names = block.partition("\n")
        stamp, _, subject = head.partition("\x1f")
        yield int(stamp), subject, [n.strip() for n in names.splitlines() if n.strip()]


def rank(repo, since="90 days ago", exts=(".cs",), with_tests=False, now=None):
    repo = Path(repo)
    now = now if now is not None else time.time()
    files = {}
    for stamp, subject, names in history(repo, since):
        fix = is_fix(subject)
        for name in names:
            if exts and not name.lower().endswith(tuple(e.lower() for e in exts)):
                continue
            if not with_tests and TEST_PATH.search(name):
                continue
            row = files.setdefault(name, {"file": name, "changes": 0, "fixes": 0, "last": 0})
            row["changes"] += 1
            row["fixes"] += fix
            row["last"] = max(row["last"], stamp)
    rows = [r for r in files.values() if (repo / r["file"]).exists()]
    for r in rows:
        r["score"] = r["changes"] + 2 * r["fixes"]
        r["days_ago"] = max(0, int((now - r["last"]) // 86400))
        del r["last"]
    return sorted(rows, key=lambda r: (-r["score"], r["days_ago"], r["file"]))


def main(argv=None):
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("repo")
    p.add_argument("--since", default="90 days ago")
    p.add_argument("--ext", action="append", help="file endings to count (default .cs; repeat for more, '' for all)")
    p.add_argument("--with-tests", action="store_true", help="count test files too")
    p.add_argument("--top", type=int, default=15)
    p.add_argument("--json", action="store_true")
    a = p.parse_args(argv)
    exts = tuple(e for e in (a.ext if a.ext is not None else [".cs"]) if e)
    rows = rank(a.repo, a.since, exts, a.with_tests)[: a.top]
    if a.json:
        print(json.dumps({"since": a.since, "files": rows}, ensure_ascii=False, indent=2))
        return 0
    if not rows:
        print(f"No matching file changed since {a.since}.")
        return 0
    print(f"Where to look first (since {a.since}; score = changes + 2 x fixes)\n")
    print("| File | Changes | Fixes | Last changed | Score |")
    print("|---|---|---|---|---|")
    for r in rows:
        print(f"| {r['file']} | {r['changes']} | {r['fixes']} | {r['days_ago']} days ago | {r['score']} |")
    return 0


if __name__ == "__main__":
    sys.exit(main())
