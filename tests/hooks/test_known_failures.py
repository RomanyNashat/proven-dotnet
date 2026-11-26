"""Tests already red before a change, kept per repo in .claude/known-failing-tests.txt (known-failures.py).

Story tests drive the hooks the way Claude Code does: SessionStart through session-start.py, test runs
through tdd-gate.py post, in a real git repo made for each test. The recheck runs against a fake runner
that prints what `dotnet test` prints.
"""
import importlib.util
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
HOOKS = os.path.join(HERE, "..", "..", ".claude", "hooks")
GATE = os.path.join(HOOKS, "tdd-gate.py")
START = os.path.join(HOOKS, "session-start.py")
_spec = importlib.util.spec_from_file_location("known_failures", os.path.join(HOOKS, "known-failures.py"))
kf = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(kf)

OUTBOX = "Visits.Infrastructure.Tests.OutboxTests"
OLD_RED = (f"Failed {OUTBOX}.Relay_BrokerDown_Retries [3 s]\n"
           f"Failed {OUTBOX}.Relay_TwoPods_OneSends [1 s]\n"
           "Failed!  - Failed:     2, Passed:    40, Skipped:     0, Total:    42, Duration: 9 s")
ONE_LEFT = (f"Failed {OUTBOX}.Relay_TwoPods_OneSends [1 s]\n"
            "Failed!  - Failed:     1, Passed:    41, Skipped:     0, Total:    42, Duration: 9 s")
GREEN = "Passed!  - Failed:     0, Passed:    42, Skipped:     0, Total:    42, Duration: 9 s"


def git(repo, *args):
    return subprocess.run(["git", "-C", repo, "-c", "user.name=t", "-c", "user.email=t@example.com", *args],
                          capture_output=True, text=True, check=True).stdout.strip()


class Repo:
    """A service repo with one commit, and one main session in it."""

    def __init__(self, folder):
        self.folder = folder
        self.root = os.path.join(folder, "visits-service")
        os.makedirs(os.path.join(self.root, "src"))
        with open(os.path.join(self.root, "Visits.sln"), "w", encoding="utf-8") as f:
            f.write("solution\n")
        with open(os.path.join(self.root, "src", "CancelVisit.cs"), "w", encoding="utf-8") as f:
            f.write("class CancelVisit {}\n")
        git(self.root, "init", "-q", "-b", "master")
        git(self.root, "add", ".")
        git(self.root, "commit", "-q", "-m", "first")
        self.list = os.path.join(self.root, ".claude", "known-failing-tests.txt")
        self.session = "s-1"

    def send(self, script, event, mode=None):
        event = {"session_id": self.session, "cwd": self.root, **event}
        env = dict(os.environ, PROVEN_TDD_GATE_DIR=self.folder, PROVEN_HOOK_PROFILE="minimal",
                   HOME=self.folder, USERPROFILE=self.folder)
        argv = [sys.executable, script] + ([mode] if mode else [])
        return subprocess.run(argv, input=json.dumps(event), capture_output=True, text=True, encoding="utf-8", env=env)

    def start(self):
        done = self.send(START, {"hook_event_name": "SessionStart", "source": "startup"})
        return json.loads(done.stdout)["hookSpecificOutput"]["additionalContext"] if done.stdout.strip() else ""

    def test_run(self, output, command="dotnet test", agent=None):
        event = {"hook_event_name": "PostToolUseFailure" if "Failed!" in output else "PostToolUse",
                 "tool_name": "Bash", "tool_input": {"command": command}}
        if agent:
            event["agent_id"], event["agent_type"] = agent, "tdd-guide"
        if "Failed!" in output:
            event["error"] = "Exit code 1\n" + output
        else:
            event["tool_response"] = {"stdout": output, "stderr": ""}
        done = self.send(GATE, event, "post")
        return json.loads(done.stdout)["hookSpecificOutput"]["additionalContext"] if done.stdout.strip() else ""

    def edit(self, rel="src/CancelVisit.cs"):
        path = os.path.join(self.root, rel)
        return self.send(GATE, {"hook_event_name": "PostToolUse", "tool_name": "Edit",
                                "tool_input": {"file_path": path, "old_string": "a", "new_string": "b"}}, "post")

    def listed(self):
        with open(self.list, encoding="utf-8") as f:
            return f.read()

    def commit_change(self):
        with open(os.path.join(self.root, "src", "CancelVisit.cs"), "a", encoding="utf-8") as f:
            f.write("// fix\n")
        git(self.root, "commit", "-q", "-am", "fix the relay")
        self.session = "s-2"  # the next session


class RecordedBeforeTheChange(unittest.TestCase):
    def setUp(self):
        self.folder = tempfile.mkdtemp(prefix="known failures ")
        self.r = Repo(self.folder)

    def tearDown(self):
        shutil.rmtree(self.folder, ignore_errors=True)

    def test_story_the_first_session_in_a_repo_is_asked_for_one_run_and_the_list_is_written(self):
        # Given a .NET repo with no list yet: the session is asked for one full run before the first edit
        note = self.r.start()
        self.assertIn("before the first C# edit", note)
        self.assertIn("dotnet test", note)
        # When it runs and two tests fail on the untouched tree
        said = self.r.test_run(OLD_RED)
        # Then the list holds both, with the commit it was made at, and git won't offer the file
        text = self.r.listed()
        self.assertIn(f"{OUTBOX}.Relay_BrokerDown_Retries", text)
        self.assertIn(f"{OUTBOX}.Relay_TwoPods_OneSends", text)
        self.assertRegex(text, r"# full run: [0-9a-f]{40} on master, \d{4}-\d{2}-\d{2}")
        self.assertIn("2 tests", said)
        self.assertEqual("", git(self.r.root, "status", "--porcelain"))
        # And the next session at the same commit isn't asked again
        self.r.session = "s-2"
        self.assertNotIn("before the first C# edit", self.r.start())

    def test_story_after_the_commit_moves_the_next_full_run_drops_what_was_fixed(self):
        self.r.test_run(OLD_RED)
        first = self.r.listed()
        since = re.search(r"Relay_TwoPods_OneSends\s+# red since (\S+)", first).group(1)
        # Given someone fixed one of them on master
        self.r.commit_change()
        # When the next session starts, it is asked to refresh, and the run shows one still red
        self.assertIn("refresh", self.r.start())
        said = self.r.test_run(ONE_LEFT)
        # Then the fixed one is gone, the other keeps the day it was first seen red
        text = self.r.listed()
        self.assertNotIn("Relay_BrokerDown_Retries", text)
        self.assertIn(f"Relay_TwoPods_OneSends  # red since {since}", text)
        self.assertIn("Relay_BrokerDown_Retries", said)
        self.assertIn(git(self.r.root, "rev-parse", "HEAD"), text)

    def test_a_green_full_run_writes_an_empty_list_so_the_next_session_isnt_asked(self):
        self.r.test_run(GREEN)
        self.assertNotIn("Failed", self.r.listed())
        self.r.session = "s-2"
        self.assertEqual("", self.r.start())

    def test_uncommitted_changes_keep_the_list_from_being_written(self):
        with open(os.path.join(self.r.root, "src", "CancelVisit.cs"), "a", encoding="utf-8") as f:
            f.write("// work in progress\n")
        said = self.r.test_run(OLD_RED)
        self.assertFalse(os.path.exists(self.r.list))
        self.assertIn("uncommitted", said)

    def test_a_run_after_the_first_edit_writes_nothing(self):
        self.r.edit()
        self.r.test_run(OLD_RED)
        self.assertFalse(os.path.exists(self.r.list))

    def test_a_run_inside_an_agent_writes_nothing(self):
        self.r.test_run(OLD_RED, agent="a1")
        self.assertFalse(os.path.exists(self.r.list))

    def test_a_filtered_run_adds_names_but_never_drops_them_or_counts_as_full(self):
        self.r.test_run(ONE_LEFT, command='dotnet test --filter "FullyQualifiedName~Outbox"')
        self.assertIn("Relay_TwoPods_OneSends", self.r.listed())
        self.assertNotIn("# full run:", self.r.listed())
        self.r.session = "s-2"
        self.assertIn("before the first C# edit", self.r.start())

    def test_a_build_that_doesnt_compile_writes_nothing(self):
        self.r.test_run(OLD_RED + "\nX.cs(1,1): error CS0246: missing\nBuild FAILED.")
        self.assertFalse(os.path.exists(self.r.list))

    def test_a_folder_with_no_dotnet_project_is_never_asked(self):
        os.remove(os.path.join(self.r.root, "Visits.sln"))
        shutil.rmtree(os.path.join(self.r.root, "src"))
        self.assertEqual("", self.r.start())


class Recheck(unittest.TestCase):
    def setUp(self):
        self.folder = tempfile.mkdtemp(prefix="known failures recheck ")
        self.r = Repo(self.folder)
        self.r.test_run(OLD_RED)
        self.commands = []

    def tearDown(self):
        shutil.rmtree(self.folder, ignore_errors=True)

    def runner(self, output):
        def run(argv, cwd):
            self.commands.append((argv, cwd))
            return output
        return run

    def test_story_recheck_runs_only_the_listed_tests_and_drops_the_ones_that_pass(self):
        report = kf.recheck(self.r.root, self.runner(ONE_LEFT))
        argv, cwd = self.commands[0]
        self.assertEqual(["dotnet", "test", "--filter"], argv[:3])
        self.assertEqual(f"FullyQualifiedName~{OUTBOX}.Relay_BrokerDown_Retries|"
                         f"FullyQualifiedName~{OUTBOX}.Relay_TwoPods_OneSends", argv[3])
        self.assertEqual(os.path.realpath(self.r.root), os.path.realpath(cwd))
        self.assertNotIn("Relay_BrokerDown_Retries", self.r.listed())
        self.assertIn("Relay_TwoPods_OneSends", self.r.listed())
        self.assertIn("fixed: " + f"{OUTBOX}.Relay_BrokerDown_Retries", report)
        self.assertIn("still red", report)

    def test_a_recheck_that_didnt_build_changes_nothing(self):
        before = self.r.listed()
        report = kf.recheck(self.r.root, self.runner("X.cs(1,1): error CS0246: missing\nBuild FAILED."))
        self.assertEqual(before, self.r.listed())
        self.assertIn("didn't build", report)

    def test_a_recheck_that_ran_no_test_changes_nothing(self):
        before = self.r.listed()
        report = kf.recheck(self.r.root, self.runner("No test matches the given testcase filter"))
        self.assertEqual(before, self.r.listed())
        self.assertIn("no test ran", report)

    def test_the_target_goes_after_test(self):
        kf.recheck(self.r.root, self.runner(GREEN), target="Visits.sln")
        self.assertEqual(["dotnet", "test", "Visits.sln", "--filter"], self.commands[0][0][:4])


class Reading(unittest.TestCase):
    def test_a_line_comment_and_a_theory_case_still_match(self):
        names = kf.names_in("# header\nA.B.C  # red since 2026-11-26\n\nA.B.D\n")
        self.assertEqual({"A.B.C", "A.B.D"}, names)
        self.assertTrue(kf.is_known('A.B.C(value: "x") ', names))
        self.assertFalse(kf.is_known("A.B.CC", names))

    def test_full_runs_are_unfiltered_whole_solution_runs(self):
        self.assertTrue(kf.full_run("dotnet test"))
        self.assertTrue(kf.full_run("cd svc && dotnet test Visits.sln --no-restore -v q"))
        self.assertFalse(kf.full_run('dotnet test --filter "X"'))
        self.assertFalse(kf.full_run("dotnet test tests/Visits.Tests"))


class SettingsWiring(unittest.TestCase):
    def test_session_start_runs_in_line_so_its_note_reaches_the_session(self):
        with open(os.path.join(HOOKS, "..", "settings.json"), encoding="utf-8") as f:
            settings = json.load(f)
        starts = [h for m in settings["hooks"]["SessionStart"] for h in m["hooks"] if "session-start.py" in h["command"]]
        self.assertEqual([False], [h.get("async", False) for h in starts])


if __name__ == "__main__":
    unittest.main()
