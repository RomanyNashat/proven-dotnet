#!/usr/bin/env python3
"""Tests that were already red before any change in a repo, kept in its `.claude/known-failing-tests.txt`.

The file is local: its path goes in the repo's `.git/info/exclude`, so git never offers it and it never
shows in a diff. The TDD gate (tdd-gate.py) and the session start (session-start.py) use this module:

- At session start in a .NET repo with no list, or a list made at another commit, the session is asked to
  run `dotnet test` once, unfiltered, before its first C# edit.
- That run, made before anything was edited on a tree with no uncommitted change, writes the list: what
  fails is known, with the day it was first seen red; what passes now drops off. A filtered run only adds.
- The done gate counts a red run whose failures are all known as green.

    python ~/.claude/hooks/proven/known-failures.py list [--repo DIR]
    python ~/.claude/hooks/proven/known-failures.py recheck [--repo DIR] [--target SLN_OR_PROJECT]

`recheck` runs only the listed tests and drops the ones that pass now. Behind /known-failures.
"""
from __future__ import annotations

import argparse
import re
import shlex
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path
from typing import Callable

LIST = Path(".claude") / "known-failing-tests.txt"
EXCLUDE = "/.claude/known-failing-tests.txt"
GREEN = re.compile(r"\bPassed!|\bTest Run Successful\b")
COMPILE_ERROR = re.compile(r"\berror CS\d{4}\b|\bBuild FAILED\b")
# One failing test per line: vstest ("Failed Ns.Class.Test [12 ms]"), the xUnit runner ("Ns.Class.Test [FAIL]")
# and Microsoft.Testing.Platform ("failed Ns.Class.Test (12ms)").
FAILED_TEST = re.compile(r"^\s*(?:Failed (?P<vstest>.+?) \[[^\]]*\]"
                         r"|(?:\[xUnit\.net [^\]]*\]\s*)?(?P<xunit>\S.*?) \[FAIL\]"
                         r"|failed (?P<mtp>.+?) \(\d[^)]*\))\s*$", re.M)
FULL_HEADER = re.compile(r"^# full run: (?P<sha>[0-9a-f]{40}) on (?P<branch>\S+), (?P<day>\S+)", re.M)
ENTRY = re.compile(r"^(?P<name>[^#\s][^#]*?)\s*(?:#\s*red since (?P<since>\S+).*)?$")
Runner = Callable[[list, str], str]


def failing_tests(text: str) -> list[str]:
    return [next(g for g in m.groups() if g) for m in FAILED_TEST.finditer(text.replace("\r\n", "\n"))]


def base(name: str) -> str:
    """A theory case's name without its arguments: the method covers each case."""
    return name.split("(", 1)[0].strip()


def is_known(name: str, known: set[str]) -> bool:
    return name.strip() in known or base(name) in known


def names_in(text: str) -> set[str]:
    return set(entries(text))


def entries(text: str) -> dict[str, str]:
    found = {}
    for line in text.splitlines():
        m = ENTRY.match(line.strip())
        if m:
            found[m["name"].strip()] = m["since"] or ""
    return found


def repo_root(start: str) -> Path | None:
    try:
        here = Path(start).resolve()
    except (OSError, ValueError):
        return None
    for folder in (here, *here.parents):
        if (folder / ".git").exists():
            return folder
    return None


def git_dir(root: Path) -> Path | None:
    dot = root / ".git"
    if dot.is_dir():
        return dot
    try:  # a worktree or a submodule: .git is a file naming the folder
        line = dot.read_text(encoding="utf-8").strip()
    except OSError:
        return None
    return (root / line[len("gitdir:"):].strip()).resolve() if line.startswith("gitdir:") else None


def head(root: Path) -> tuple[str, str] | None:
    """(commit, branch) read from the files, with no git process: the session start must stay quick."""
    gd = git_dir(root)
    if gd is None:
        return None
    try:
        ref = (gd / "HEAD").read_text(encoding="utf-8").strip()
    except OSError:
        return None
    if not ref.startswith("ref: "):
        return (ref, "detached") if re.fullmatch(r"[0-9a-f]{40}", ref) else None
    ref = ref[5:]
    common = common_dir(gd)
    for folder in (gd, common):
        try:
            return (folder / ref).read_text(encoding="utf-8").strip(), ref.rsplit("/", 1)[-1]
        except OSError:
            pass
    try:
        for line in (common / "packed-refs").read_text(encoding="utf-8").splitlines():
            if line.endswith(" " + ref):
                return line.split(" ", 1)[0], ref.rsplit("/", 1)[-1]
    except OSError:
        pass
    return None


def common_dir(gd: Path) -> Path:
    try:
        return (gd / (gd / "commondir").read_text(encoding="utf-8").strip()).resolve()
    except OSError:
        return gd


def is_dotnet(root: Path) -> bool:
    if any(root.glob("*.sln")) or any(root.glob("*.slnx")):
        return True
    return any(p for depth in ("*", "*/*", "*/*/*") for p in root.glob(depth + "/*.csproj"))


def uncommitted(root: Path) -> bool | None:
    """Whether tracked or new files changed, leaving out .claude/. None when git can't say."""
    try:
        done = subprocess.run(["git", "-C", str(root), "status", "--porcelain"], capture_output=True, text=True,
                              encoding="utf-8", timeout=20)
    except (OSError, subprocess.SubprocessError):
        return None
    if done.returncode != 0:
        return None
    return any(line[3:].strip('"') and not line[3:].strip('"').startswith(".claude/") for line in done.stdout.splitlines())


def full_run(command: str) -> bool:
    """`dotnet test` over the whole solution: no filter, and no project or folder named but a solution."""
    try:
        words = shlex.split(command, posix=True)
    except ValueError:
        return False
    for i, word in enumerate(words):
        if re.fullmatch(r"dotnet(\.exe)?", word) and i + 1 < len(words) and words[i + 1] == "test":
            rest, j = words[i + 2:], 0
            while j < len(rest) and rest[j] not in ("&&", ";", "|", "||"):
                arg = rest[j]
                if arg.startswith("--filter") or arg.startswith("--filter-"):
                    return False
                if not arg.startswith("-") and not re.search(r"\.slnx?$", arg) and not prev_takes_value(rest, j):
                    return False
                j += 1
            return True
    return False


def prev_takes_value(words: list[str], j: int) -> bool:
    return j > 0 and words[j - 1] in ("-v", "--verbosity", "-c", "--configuration", "-f", "--framework",
                                      "--logger", "-l", "--results-directory", "-r", "--runtime", "-s", "--settings")


def today() -> str:
    return datetime.now(timezone.utc).date().isoformat()


def read(root: Path) -> tuple[str, dict[str, str]]:
    try:
        text = (root / LIST).read_text(encoding="utf-8-sig")
    except (OSError, ValueError):
        return "", {}
    return text, entries(text)


def listed(cwd: str) -> set[str]:
    root = repo_root(cwd) or (Path(cwd) if cwd else None)
    return set(read(root)[1]) if root else set()


def write(root: Path, names: dict[str, str], header: str) -> None:
    path = root / LIST
    path.parent.mkdir(parents=True, exist_ok=True)
    lines = ["# Tests already red before any change here, written by the TDD gate. Local: git never offers it.",
             "# Each one passing again drops off at the next full run, or with /known-failures.", header]
    lines += [f"{name}  # red since {since}" for name, since in sorted(names.items())]
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")
    exclude(root)


def exclude(root: Path) -> None:
    gd = git_dir(root)
    if gd is None:
        return
    target = common_dir(gd) / "info" / "exclude"
    try:
        current = target.read_text(encoding="utf-8") if target.exists() else ""
        if EXCLUDE not in current.split():
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text(current + ("" if current.endswith("\n") or not current else "\n") + EXCLUDE + "\n",
                              encoding="utf-8")
    except OSError:
        pass


def record_baseline(cwd: str, command: str, text: str) -> str | None:
    """A run made before anything was edited. Returns what to tell the session, or None."""
    root = repo_root(cwd)
    if root is None or COMPILE_ERROR.search(text):
        return None
    failing = {base(n) for n in failing_tests(text)}
    if not failing and not GREEN.search(text):
        return None
    dirty = uncommitted(root)
    if dirty is None:
        return None
    if dirty:
        return (f"Not written to {LIST.as_posix()}: the tree has uncommitted changes, so a red test may be one of "
                "them. The list is made from a tree with nothing uncommitted.")
    old_text, old = read(root)
    day = today()
    if full_run(command):
        at = head(root)
        header = f"# full run: {at[0]} on {at[1]}, {day}" if at else "# full run: unknown commit"
        names = {n: old.get(n) or day for n in failing}
        write(root, names, header)
        fixed = sorted(set(old) - failing)
        said = f"{len(names)} tests already red here are in {LIST.as_posix()}; they don't hold the turn."
        return said + (f" Passing again, so dropped: {', '.join(fixed)}." if fixed else "")
    added = failing - set(old)
    if not added:
        return None
    match = FULL_HEADER.search(old_text)
    header = match.group(0) if match else "# no full run yet"
    write(root, {**old, **{n: day for n in added}}, header)
    return f"Added to {LIST.as_posix()} as already red: {', '.join(sorted(added))}."


def start_note(cwd: str) -> str:
    root = repo_root(cwd)
    if root is None or not is_dotnet(root):
        return ""
    text, names = read(root)
    match = FULL_HEADER.search(text)
    at = head(root)
    ask = ("Run `dotnet test` once, unfiltered, before the first C# edit, on the tree as it is: the TDD gate "
           f"writes what already fails to {LIST.as_posix()} (local, never committed), so those tests don't "
           "hold your turns. Skip it if the developer says the suite is too slow, and say so.")
    if not match:
        return f"This repo has no list yet of tests already red. {ask}"
    if at and match["sha"] != at[0]:
        return f"The list of tests already red was made at {match['sha'][:8]}; HEAD is {at[0][:8]}. To refresh it: {ask}"
    return f"{len(names)} tests here were already red at this commit ({LIST.as_posix()})." if names else ""


def dotnet(argv: list, cwd: str) -> str:
    done = subprocess.run(argv, cwd=cwd, capture_output=True, text=True, encoding="utf-8", errors="replace")
    return done.stdout + "\n" + done.stderr


def recheck(repo: str, run: Runner = dotnet, target: str | None = None) -> str:
    root = repo_root(repo) or Path(repo)
    text, names = read(root)
    if not names:
        return f"Nothing listed in {LIST.as_posix()}."
    query = "|".join(f"FullyQualifiedName~{n}" for n in sorted(names))
    output = run(["dotnet", "test", *([target] if target else []), "--filter", query], str(root))
    if COMPILE_ERROR.search(output):
        return "The tests didn't build, so the list is unchanged. Fix the build and run it again."
    failing = {base(n) for n in failing_tests(output)}
    if not failing and not GREEN.search(output):
        return "no test ran (the filter matched nothing, or the output had no summary), so the list is unchanged."
    still = {n: s for n, s in names.items() if is_known_any(n, failing)}
    fixed = sorted(set(names) - set(still))
    match = FULL_HEADER.search(text)
    write(root, still, match.group(0) if match else "# no full run yet")
    lines = [f"fixed: {n}" for n in fixed]
    lines += [f"still red: {n}  (since {s or 'unknown'})" for n, s in sorted(still.items())]
    return "\n".join(lines) or "Nothing changed."


def is_known_any(listed_name: str, failing: set[str]) -> bool:
    return listed_name in failing or any(f.startswith(listed_name + ".") for f in failing)


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description="Tests already red before any change in this repo.")
    ap.add_argument("action", choices=["list", "recheck"])
    ap.add_argument("--repo", default=".")
    ap.add_argument("--target", help="the solution or project to test, when the folder has more than one")
    args = ap.parse_args(argv)
    root = repo_root(args.repo) or Path(args.repo)
    if args.action == "list":
        text, _ = read(root)
        print(text.strip() or f"Nothing listed in {LIST.as_posix()}.")
        return 0
    print(recheck(str(root), target=args.target))
    return 0


if __name__ == "__main__":
    sys.exit(main())
