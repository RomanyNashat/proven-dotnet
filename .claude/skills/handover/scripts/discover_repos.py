#!/usr/bin/env python3
"""List the git repos under a folder where YOU committed since a date — the /handover repo picker.

Read-only: runs only `git config`, `git log`, `git status`, `git rev-parse`. Never fetches, pushes or writes.

Usage:
    python discover_repos.py [ROOT] --since 2026-06-01 [--depth 3] [--author "extra@mail"] [--json]

For each repo it reports: your commit count since the date, your last commit date, the current
branch, uncommitted files, and commits that were never pushed. Repos with no commits from you are
listed separately (count only) so nothing is hidden.
"""
import argparse
import json
import os
import subprocess
import sys

SKIP_DIRS = {"node_modules", "bin", "obj", "packages", "TestResults"}


def git(args, cwd):
    try:
        r = subprocess.run(["git", *args], cwd=cwd, capture_output=True, text=True,
                           encoding="utf-8", errors="replace", timeout=120)
    except (OSError, subprocess.TimeoutExpired):
        return ""
    return r.stdout.strip() if r.returncode == 0 else ""


def find_repos(root, max_depth):
    repos = []
    for dirpath, dirnames, filenames in os.walk(root):
        rel = os.path.relpath(dirpath, root)
        depth = 0 if rel == "." else rel.count(os.sep) + 1
        if ".git" in dirnames or ".git" in filenames:
            repos.append(dirpath)
            dirnames[:] = []            # a repo's own subfolders are not separate repos
            continue
        if depth >= max_depth:
            dirnames[:] = []
            continue
        dirnames[:] = [d for d in dirnames if d not in SKIP_DIRS and not d.startswith(".")]
    return sorted(repos, key=str.lower)


def identities(repo, extra):
    ids = [git(["config", "user.email"], repo), git(["config", "user.name"], repo), *extra]
    return [i for i in dict.fromkeys(ids) if i]


def analyse(repo, since, extra_authors):
    authors = identities(repo, extra_authors)
    author_args = [f"--author={a}" for a in authors]     # several --author flags are OR-ed by git
    log = git(["log", "--all", f"--since={since}", *author_args, "--format=%H|%ad", "--date=short"], repo) if authors else ""
    commits = [line.split("|") for line in log.splitlines() if "|" in line]
    unpushed = git(["log", "--branches", "--not", "--remotes", "--oneline"], repo)
    dirty = git(["status", "--porcelain"], repo)
    return {
        "repo": repo,
        "name": os.path.basename(repo.rstrip("\\/")),
        "my_commits": len({c[0] for c in commits}),
        "last_commit": max((c[1] for c in commits), default=None),
        "branch": git(["rev-parse", "--abbrev-ref", "HEAD"], repo),
        "uncommitted_files": len(dirty.splitlines()) if dirty else 0,
        "unpushed_commits": len(unpushed.splitlines()) if unpushed else 0,
        "matched_as": authors,
    }


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("root", nargs="?", default=os.getcwd())
    p.add_argument("--since", required=True, help="YYYY-MM-DD")
    p.add_argument("--depth", type=int, default=3, help="how many folder levels to search (default 3)")
    p.add_argument("--author", action="append", default=[], help="extra name/email you commit as")
    p.add_argument("--json", action="store_true")
    a = p.parse_args()

    results = [analyse(r, a.since, a.author) for r in find_repos(os.path.abspath(a.root), a.depth)]
    mine = sorted((r for r in results if r["my_commits"]), key=lambda r: r["last_commit"] or "", reverse=True)
    others = [r for r in results if not r["my_commits"]]

    if a.json:
        json.dump({"since": a.since, "root": os.path.abspath(a.root), "mine": mine, "no_commits_from_you": others},
                  sys.stdout, indent=1)
        return

    print(f"Repos under {os.path.abspath(a.root)} with your commits since {a.since}: {len(mine)}\n")
    print(f"{'#':>3}  {'repo':<40} {'commits':>7}  {'last':<10}  {'branch':<30} {'uncommitted':>11} {'unpushed':>8}")
    for i, r in enumerate(mine, 1):
        print(f"{i:>3}  {r['name'][:40]:<40} {r['my_commits']:>7}  {r['last_commit'] or '':<10}  "
              f"{r['branch'][:30]:<30} {r['uncommitted_files']:>11} {r['unpushed_commits']:>8}")
    if others:
        print(f"\n{len(others)} other repo(s) with no commits from you since {a.since} (not listed).")


if __name__ == "__main__":
    main()
