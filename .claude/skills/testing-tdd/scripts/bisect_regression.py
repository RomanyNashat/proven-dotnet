#!/usr/bin/env python3
"""Finds the commit that broke a test, without moving the developer's checkout.

    python bisect_regression.py --good REF [--bad REF] [--build "CMD"] -- TEST COMMAND ...

The test command passes on GOOD and fails on BAD (default HEAD): usually one `dotnet test --filter`, the
reproducing test written first. The search runs `git bisect` in a throwaway worktree at BAD, so the
developer's branch, working tree and uncommitted changes never move; the worktree is removed at the end,
whatever happens. A commit whose BUILD command fails is skipped (exit 125 to git), not blamed: a broken
build in the middle of history isn't the regression being looked for.

It prints the first bad commit (hash, author, date, subject, files) and the commits it had to skip. Exit 0
when it found one; 1 when the test doesn't fail on BAD or doesn't pass on GOOD (nothing to bisect); 2 when
skipped commits left it between several.
"""
from __future__ import annotations

import argparse
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path


def git(*args: str, cwd: Path, check: bool = True) -> subprocess.CompletedProcess:
    return subprocess.run(["git", *args], cwd=cwd, capture_output=True, text=True, encoding="utf-8", errors="replace", check=check)


def run(cmd: list[str] | str, cwd: Path) -> int:
    shell = isinstance(cmd, str)
    return subprocess.run(cmd, cwd=cwd, shell=shell, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL).returncode


def step(build: str | None, test: list[str]) -> int:
    """One bisect step, run by git in the worktree: 125 skips the commit, 0 is good, 1 is bad."""
    here = Path.cwd()
    if build and run(build, here) != 0:
        return 125
    return 0 if run(test, here) == 0 else 1


def main(argv=None) -> int:
    argv = list(sys.argv[1:] if argv is None else argv)
    test = argv[argv.index("--") + 1:] if "--" in argv else []
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("--good", required=False)
    p.add_argument("--bad", default="HEAD")
    p.add_argument("--build")
    p.add_argument("--repo", type=Path, default=Path("."))
    p.add_argument("--step", type=Path, help=argparse.SUPPRESS)
    a = p.parse_args(argv[:argv.index("--")] if "--" in argv else argv)
    if a.step:
        # The commands come from a file: passed through git and a Windows shell, quotes and spaces don't survive.
        cfg = json.loads(a.step.read_text(encoding="utf-8"))
        return step(cfg["build"], cfg["test"])
    if not test:
        p.error("give the test command after --")
    if not a.good:
        p.error("--good is required")

    top = Path(git("rev-parse", "--show-toplevel", cwd=a.repo).stdout.strip())
    good = git("rev-parse", a.good, cwd=top).stdout.strip()
    bad = git("rev-parse", a.bad, cwd=top).stdout.strip()
    tree = Path(tempfile.mkdtemp(prefix="bisect-")) / "tree"
    git("worktree", "add", "--detach", str(tree), bad, cwd=top)
    try:
        if a.build and run(a.build, tree) != 0:
            print(f"The build fails on {a.bad}: fix the build first, or pick a bad commit that builds.")
            return 1
        if run(test, tree) == 0:
            print(f"The test passes on {a.bad}: there's nothing to bisect. Check the command reproduces the failure.")
            return 1
        git("checkout", "--quiet", "--detach", good, cwd=tree)
        if (a.build and run(a.build, tree) != 0) or run(test, tree) != 0:
            print(f"The test doesn't pass on {a.good}: pick an older good commit.")
            return 1
        git("bisect", "start", bad, good, cwd=tree)
        cfg = tree.parent / "step.json"
        cfg.write_text(json.dumps({"build": a.build, "test": test}), encoding="utf-8")
        out = git("bisect", "run", sys.executable, str(Path(__file__).resolve()), "--step", str(cfg), cwd=tree, check=False).stdout
        skipped = git("bisect", "log", cwd=tree, check=False).stdout
        skipped = sorted(set(re.findall(r"^git bisect skip ([0-9a-f]{40})", skipped, re.M)))
        found = re.search(r"^([0-9a-f]{40}) is the first bad commit", out, re.M)
        if not found:
            print("Skipped commits left it between several; the first bad commit is one of these:")
            for line in out.splitlines():
                if re.match(r"^[0-9a-f]{40}$", line.strip()):
                    print(f"- {git('log', '-1', '--format=%h %s', line.strip(), cwd=top).stdout.strip()}")
            return 2
        sha = found.group(1)
        info = git("show", "--no-patch", "--format=%h|%an|%ad|%s", "--date=short", sha, cwd=top).stdout.strip().split("|", 3)
        files = git("show", "--format=", "--name-only", sha, cwd=top).stdout.split()
        print(f"First bad commit: {info[0]} {info[3]}\nAuthor: {info[1]}, {info[2]}\nFiles: {', '.join(files)}")
        if skipped:
            print("Skipped (the build failed): " + ", ".join(s[:7] for s in skipped))
        return 0
    finally:
        git("bisect", "reset", cwd=tree, check=False)
        git("worktree", "remove", "--force", str(tree), cwd=top, check=False)
        shutil.rmtree(tree.parent, ignore_errors=True)


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.exit(main())
