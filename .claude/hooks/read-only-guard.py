#!/usr/bin/env python3
"""PreToolUse guard for the read-only agents that need a shell: every Bash command they send must only
read. Wired in the agent's own frontmatter, so it runs inside that agent and nowhere else (no launch on
the main session's commands).

    {PYTHON} {HOOKS}/read-only-guard.py <policy>      policy: repo-reader | health

repo-reader (handover-repo-analyst): git read commands, file reads and searches, the harness's own skill
scripts. health (health-analyst): the same, plus `dotnet build`/`test`/`list`/`restore`, and writes only
into a `coverage/` folder (the report card's results, `build.log`, `health-history.json`).

A command is allowed when every command in it, including those inside `$( )` and backticks, is on the
list for the policy and no redirect writes outside what the policy allows. Anything else, or anything
that can't be parsed, is refused (exit 2, the reason on stderr, which the agent reads). It fails closed.
"""
from __future__ import annotations

import json
import re
import shlex
import sys

READERS = {
    "ls", "cat", "head", "tail", "wc", "grep", "egrep", "fgrep", "rg", "sort", "uniq", "cut", "tr",
    "echo", "printf", "test", "[", "true", "false", "pwd", "date", "basename", "dirname", "realpath",
    "readlink", "stat", "file", "du", "jq", "diff", "cmp", "comm", "column", "nl", "tac", "which",
}
GIT_READ = {
    "log", "show", "diff", "status", "blame", "rev-parse", "rev-list", "ls-files", "ls-tree", "cat-file",
    "shortlog", "describe", "for-each-ref", "check-ignore", "grep", "merge-base", "name-rev", "count-objects",
}
GIT_BRANCH_VALUE_OPTIONS = {"--merged", "--no-merged", "--contains", "--no-contains", "--points-at", "--sort", "--format"}
GIT_BRANCH_WRITES = {"-d", "-D", "-m", "-M", "-c", "-C", "-f", "-u", "--delete", "--move", "--copy", "--force",
                     "--set-upstream-to", "--unset-upstream", "--edit-description", "--create-reflog"}
DOTNET_READ = {"build", "test", "list", "restore", "--info", "--version", "--list-sdks", "--list-runtimes"}
FIND_WRITES = {"-exec", "-execdir", "-ok", "-okdir", "-delete", "-fprint", "-fprint0", "-fprintf", "-fls"}
KEYWORDS = {"do", "then", "else", "elif", "if", "while", "until", "!", "{", "(", "time"}
ENDS = {"done", "fi", "}", ")", "esac"}
SUBST = re.compile(r"\$\(([^()]*)\)|`([^`]*)`")


class Refused(Exception):
    pass


def is_skill_script(path: str) -> bool:
    p = path.replace("\\", "/")
    return "/.claude/skills/" in p and "/scripts/" in p and p.endswith(".py") and ".." not in p.split("/")


def writes_allowed(target: str, policy: str) -> bool:
    t = target.replace("\\", "/")
    if t in ("/dev/null", "nul", "NUL"):
        return True
    if policy == "health":
        return (t.startswith("coverage/") or t.startswith("./coverage/") or "/coverage/" in t) and ".." not in t.split("/")
    return False


def check_git(args: list[str]) -> None:
    i = 0
    while i < len(args) and args[i].startswith("-"):
        if args[i] == "-C":
            i += 2
            continue
        if args[i] in ("--no-pager", "-P", "--no-optional-locks"):
            i += 1
            continue
        raise Refused(f"git option {args[i]} is not allowed here")
    if i >= len(args):
        raise Refused("git with no subcommand")
    sub, rest = args[i], args[i + 1:]
    if any(a.startswith("--output") for a in rest):
        raise Refused("git --output writes a file")
    if sub in GIT_READ:
        return
    if sub == "branch":
        if any(a in GIT_BRANCH_WRITES or a.split("=")[0] in GIT_BRANCH_WRITES for a in rest):
            raise Refused("git branch that changes a branch")
        listing = any(a in ("-l", "--list", "-a", "-r", "--all", "--remotes") for a in rest)
        j = 0
        while j < len(rest):
            a = rest[j]
            if a in GIT_BRANCH_VALUE_OPTIONS:
                j += 2
                continue
            if not a.startswith("-") and not listing:
                raise Refused("git branch <name> creates a branch")
            j += 1
        return
    if sub == "tag":
        if rest and not any(a in ("-l", "--list") for a in rest):
            raise Refused("git tag with arguments creates or deletes a tag")
        return
    if sub == "config":
        if not rest or rest[0] not in ("--get", "--get-all", "--get-regexp", "--list", "-l"):
            raise Refused("git config other than --get or --list")
        return
    if sub == "symbolic-ref":
        if len([a for a in rest if not a.startswith("-")]) > 1:
            raise Refused("git symbolic-ref with a target writes it")
        return
    if sub == "remote":
        if rest and rest[0] not in ("-v", "--verbose", "get-url", "show"):
            raise Refused("git remote that changes a remote")
        return
    if sub in ("stash", "worktree") and rest[:1] == ["list"]:
        return
    if sub == "reflog" and (not rest or rest[0] == "show" or rest[0].startswith("-")):
        return
    raise Refused(f"git {sub} is not a read command")


def check_sed(args: list[str]) -> None:
    for a in args:
        if a == "-i" or a.startswith("-i") and not a.startswith("-in") or a.startswith("--in-place"):
            raise Refused("sed -i edits a file")
    script = " ".join(a for a in args if not a.startswith("-"))
    if re.search(r"(^|[;}\n])\s*[weWr]\b|/[gpIi0-9]*w\s", script):
        raise Refused("a sed script that writes a file or runs a command")


def check_simple(words: list[str], policy: str) -> None:
    while words and re.match(r"^[A-Za-z_][A-Za-z0-9_]*=", words[0]):
        words = words[1:]
    while words and words[0] in KEYWORDS:
        words = words[1:]
    if not words or words[0] in ENDS:
        return
    prog, args = words[0], words[1:]
    name = prog.replace("\\", "/").rsplit("/", 1)[-1]
    if name.endswith(".exe"):
        name = name[:-4]
    if name == "for":
        return  # `for x in list` only binds a name; the loop body is checked as its own commands
    if name == "command" and args[:1] == ["-v"]:
        return
    if name in READERS:
        return
    if name == "git":
        return check_git(args)
    if name == "find":
        bad = FIND_WRITES.intersection(args)
        if bad:
            raise Refused(f"find {sorted(bad)[0]} runs a command or writes")
        return
    if name == "sed":
        return check_sed(args)
    if name in ("python", "python3", "py"):
        rest = args[1:] if name == "py" and args[:1] == ["-3"] else args
        if rest and is_skill_script(rest[0]):
            return
        raise Refused("python runs only the harness's own skill scripts here")
    if name == "dotnet" and policy == "health":
        if args and (args[0] in DOTNET_READ or args[:2] == ["sln", "list"]):
            return
        raise Refused("dotnet other than build, test, list or restore")
    if name == "tee" and policy == "health":
        targets = [a for a in args if not a.startswith("-")]
        if targets and all(writes_allowed(t, policy) for t in targets):
            return
        raise Refused("tee writes only into coverage/ here")
    raise Refused(f"{name} is not on this agent's read-only list")


def segments(command: str) -> list[list[str]]:
    """Splits a shell command into simple commands, checking redirects on the way."""
    lexer = shlex.shlex(command, posix=True, punctuation_chars=";&|<>()")
    lexer.whitespace_split = True
    lexer.commenters = ""
    try:
        tokens = list(lexer)
    except ValueError as e:
        raise Refused(f"the command can't be parsed ({e})")
    out, current = [], []
    i = 0
    while i < len(tokens):
        t = tokens[i]
        if t in (";", "&&", "||", "|", "&", ";;", "(", ")", "|&"):
            out.append(current)
            current = []
        elif t and set(t) <= set("<>&") and ">" in t or t in (">", ">>", ">|"):
            if current and re.fullmatch(r"[0-9]", current[-1]):
                current.pop()  # the fd number of 2> belongs to the redirect, not the command
            target = tokens[i + 1] if i + 1 < len(tokens) else ""
            if t.endswith("&") or re.fullmatch(r"&?[0-9-]", target):
                i += 2  # 2>&1 and >&2 copy a stream; they write no file
                continue
            out.append(["__write__", target])
            i += 2
            continue
        elif t in ("<", "<<", "<<<"):
            i += 2
            continue
        else:
            current.append(t)
        i += 1
    out.append(current)
    return [s for s in out if s]


def check(command: str, policy: str) -> None:
    if "\n" in command.strip() and "<<" in command:
        raise Refused("here-documents aren't allowed here")
    pending = [command]
    while pending:
        text = pending.pop()
        for m in SUBST.finditer(text):
            pending.append(m.group(1) if m.group(1) is not None else m.group(2))
        flat = SUBST.sub("SUBST", text)
        if "$(" in flat or "`" in flat:
            raise Refused("nested command substitution")
        for line in flat.replace("\r", "").split("\n"):
            for seg in segments(line):
                if seg[0] == "__write__":
                    if not writes_allowed(seg[1], policy):
                        raise Refused(f"a redirect writes to {seg[1]}")
                    continue
                check_simple(seg, policy)


def main() -> int:
    policy = sys.argv[1] if len(sys.argv) > 1 else "repo-reader"
    try:
        event = json.load(sys.stdin)
    except ValueError:
        print("read-only guard: the hook input wasn't JSON, so the command was refused", file=sys.stderr)
        return 2
    if event.get("tool_name") != "Bash":
        return 0
    command = (event.get("tool_input") or {}).get("command", "")
    try:
        check(command, policy)
    except Refused as why:
        print(f"read-only agent: refused ({why}). Use a read command; this agent never changes the repo.",
              file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main())
