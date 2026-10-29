"""Lists the harness files (skills, rules, agents, commands under .claude/) that changed since the last commit
that changed a fix eval's scores, so a reviewer sees what no eval has measured yet.

    python tools/ci/changed-since-eval.py [repo] >> "$GITHUB_STEP_SUMMARY"

Prints Markdown under "Changed since the last fix eval". It only reports: it exits 0 whatever it finds,
and when git can't answer (no history, no scores yet) it says so instead. Needs the full history
(actions/checkout with fetch-depth: 0).
"""
import subprocess
import sys

SCORES = ":(glob)tests/evals/*/scores.json"
HARNESS = [".claude/skills", ".claude/rules", ".claude/agents", ".claude/commands"]
TITLE = "### Changed since the last fix eval"


def git(repo, *args):
    done = subprocess.run(["git", "-C", repo, *args], capture_output=True, text=True)
    if done.returncode != 0:
        raise RuntimeError(done.stderr.strip() or f"git {' '.join(args)} failed")
    return done.stdout.strip()


def report(repo="."):
    try:
        last = git(repo, "log", "-1", "--format=%H%x09%h%x09%s", "--", SCORES)
        if not last:
            return f"{TITLE}\n\nNo commit has changed a fix eval's `scores.json` yet.\n"
        full, short, subject = last.split("\t", 2)
        changed = git(repo, "diff", "--no-renames", "--name-status", full, "HEAD", "--", *HARNESS).splitlines()
    except (RuntimeError, OSError) as error:
        return f"{TITLE}\n\nCouldn't tell: {error}\n"

    lines = [TITLE, "", f"Last eval scores: `{short}` {subject}", ""]
    if not changed:
        lines.append("No skill, rule, agent or command has changed since.")
    else:
        lines.append(f"{len(changed)} file(s) no fix eval has run against yet:")
        lines.append("")
        lines += [f"- `{status}` {path}" for status, path in (c.split("\t", 1) for c in changed)]
    return "\n".join(lines) + "\n"


if __name__ == "__main__":
    sys.stdout.write(report(sys.argv[1] if len(sys.argv) > 1 else "."))
