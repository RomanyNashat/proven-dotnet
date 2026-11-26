"""The TDD gate in tdd-guide's frontmatter (tdd-gate.py).

Story tests drive the hook the way Claude Code does, event by event, with the inputs a real session sent
(probed 2026-11-16 on 2.1.325): a passing Bash command reaches PostToolUse with its stdout in
tool_response, a failing one reaches PostToolUseFailure with "Exit code 1" and its output in `error`, and
the agent's Stop hook arrives as SubagentStop. Production tests use a Windows machine's shapes.
"""
import json
import os
import shutil
import subprocess
import sys
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
GATE = os.path.join(HERE, "..", "..", ".claude", "hooks", "tdd-gate.py")
REPO = "/work/visits-service"

FAILED = ("Build succeeded.\nFailed SkillSamples.CancelVisitTests.Cancel_AnHourBefore_FreesTheSlot [12 ms]\n"
          "Failed!  - Failed:     1, Passed:     4, Skipped:     0, Total:     5, Duration: 210 ms")
PASSED = "Passed!  - Failed:     0, Passed:     5, Skipped:     0, Total:     5, Duration: 190 ms"
NO_COMPILE = ("CancelVisitTests.cs(14,23): error CS0246: The type or namespace name 'CancelVisit' could not be found\n"
              "Build FAILED.")


class Session:
    """One tdd-guide run: the same session and agent id on every event, as Claude Code sends them."""

    def __init__(self, folder, agent="a400bdae", cwd=REPO):
        self.folder, self.agent, self.cwd = folder, agent, cwd

    def send(self, mode, event, env=None):
        event = {"session_id": "s-1", "agent_id": self.agent, "agent_type": "tdd-guide", "cwd": self.cwd, **event}
        environment = dict(env or os.environ, PROVEN_TDD_GATE_DIR=self.folder)
        return subprocess.run([sys.executable, GATE, mode], input=json.dumps(event), capture_output=True,
                              text=True, encoding="utf-8", env=environment)

    def edit(self, path, tool="Edit"):
        """PreToolUse, and PostToolUse when it was allowed. Returns whether the edit went through."""
        tool_input = {"file_path": path, "old_string": "a", "new_string": "b"} if tool == "Edit" else {"file_path": path, "content": "x"}
        pre = self.send("pre", {"hook_event_name": "PreToolUse", "tool_name": tool, "tool_input": tool_input})
        if pre.returncode == 0:
            self.send("post", {"hook_event_name": "PostToolUse", "tool_name": tool, "tool_input": tool_input,
                               "tool_response": {"filePath": path}})
        return pre

    def test_run(self, output, failed):
        command = 'dotnet test --filter "FullyQualifiedName~CancelVisitTests"'
        if failed:
            return self.send("post", {"hook_event_name": "PostToolUseFailure", "tool_name": "Bash",
                                      "tool_input": {"command": command}, "error": "Exit code 1\n" + output})
        return self.send("post", {"hook_event_name": "PostToolUse", "tool_name": "Bash", "tool_input": {"command": command},
                                  "tool_response": {"stdout": output, "stderr": "", "interrupted": False}})

    def stop(self):
        return self.send("stop", {"hook_event_name": "SubagentStop", "stop_hook_active": False})


class Stories(unittest.TestCase):
    def setUp(self):
        self.folder = tempfile.mkdtemp(prefix="tdd gate ")
        self.s = Session(self.folder)

    def tearDown(self):
        shutil.rmtree(self.folder, ignore_errors=True)

    def test_story_writing_the_code_before_the_test_is_stopped_at_the_first_edit(self):
        # Given tdd-guide starts on "cancel a visit" and reaches for the handler first
        pre = self.s.edit(f"{REPO}/src/Visits.Application/CancelVisit.cs", tool="Write")
        # Then the edit is refused, saying what to do first
        self.assertEqual(2, pre.returncode)
        self.assertIn("src/Visits.Application/CancelVisit.cs waits", pre.stderr)
        self.assertIn("run `dotnet test` and see it fail", pre.stderr)

    def test_story_red_green_refactor_goes_through_and_ends_green(self):
        # Given the test is written first and doesn't compile yet: red
        self.assertEqual(0, self.s.edit(f"{REPO}/tests/Visits.Tests/CancelVisitTests.cs", tool="Write").returncode)
        self.s.test_run(NO_COMPILE, failed=True)
        # When the handler is written, the tests pass, and it's tidied
        self.assertEqual(0, self.s.edit(f"{REPO}/src/Visits.Application/CancelVisit.cs", tool="Write").returncode)
        self.s.test_run(PASSED, failed=False)
        self.assertEqual(0, self.s.edit(f"{REPO}/src/Visits.Application/CancelVisit.cs").returncode)
        # Then it can't finish on the edit after the last run, and can once the run after it is green
        stop = self.s.stop()
        self.assertEqual(2, stop.returncode)
        self.assertIn("production code changed after the last test run", stop.stderr)
        self.s.test_run(PASSED, failed=False)
        self.assertEqual(0, self.s.stop().returncode)

    def test_story_tests_qa_engineer_wrote_count_once_they_are_run_red(self):
        # Given qa-engineer's failing tests are already in the repo: tdd-guide changes no test
        self.assertEqual(2, self.s.edit(f"{REPO}/src/Visits.Application/CancelVisit.cs").returncode)
        self.s.test_run(FAILED, failed=True)
        self.assertEqual(0, self.s.edit(f"{REPO}/src/Visits.Application/CancelVisit.cs").returncode)

    def test_story_it_cannot_finish_red_but_is_let_go_after_three_asks_to_report_it(self):
        self.s.test_run(FAILED, failed=True)
        self.s.edit(f"{REPO}/src/Visits.Application/CancelVisit.cs")
        self.s.test_run(FAILED, failed=True)
        asks = [self.s.stop() for _ in range(4)]
        self.assertEqual([2, 2, 2, 0], [a.returncode for a in asks])
        self.assertIn("the last test run is red", asks[0].stderr)

    def test_tests_projects_and_settings_are_never_held_back(self):
        for path in (f"{REPO}/tests/Visits.Tests/CancelVisitTests.cs", f"{REPO}/src/Visits.Tests/Fakes.cs",
                     f"{REPO}/src/Visits.Api/Visits.Api.csproj", f"{REPO}/src/Visits.Api/appsettings.json", f"{REPO}/story.md"):
            self.assertEqual(0, self.s.edit(path, tool="Write").returncode, path)
        self.assertEqual(0, self.s.stop().returncode)  # no production code changed: nothing to prove

    def test_a_run_that_found_no_tests_or_wasnt_dotnet_test_unlocks_nothing(self):
        self.s.test_run("No test matches the given testcase filter", failed=False)
        self.s.send("post", {"hook_event_name": "PostToolUseFailure", "tool_name": "Bash",
                             "tool_input": {"command": "dotnet build"}, "error": "Exit code 1\n" + NO_COMPILE})
        self.assertEqual(2, self.s.edit(f"{REPO}/src/Visits.Application/CancelVisit.cs").returncode)

    def test_each_agent_run_starts_locked(self):
        self.s.test_run(FAILED, failed=True)
        other = Session(self.folder, agent="b511cdef")
        self.assertEqual(2, other.edit(f"{REPO}/src/Visits.Application/CancelVisit.cs").returncode)

    def test_input_it_cant_read(self):
        env = dict(os.environ, PROVEN_TDD_GATE_DIR=self.folder)
        pre = subprocess.run([sys.executable, GATE, "pre"], input="not json", capture_output=True, text=True, env=env)
        self.assertEqual(2, pre.returncode)  # an edit fails closed
        stop = subprocess.run([sys.executable, GATE, "stop"], input="not json", capture_output=True, text=True, env=env)
        self.assertEqual(0, stop.returncode)  # finishing doesn't


FORMAT = os.path.join(HERE, "..", "..", ".claude", "hooks", "post-edit-format.py")


class MainSession(Session):
    """The developer's own session: no agent id, the Stop event from settings.json, and edits recorded by
    the format hook (the one launch per edit) instead of the agent's own post."""

    def __init__(self, folder, cwd=REPO):
        super().__init__(folder, cwd=cwd)

    def send(self, mode, event, env=None):
        event = {"session_id": "s-2", "cwd": self.cwd, **event}
        environment = dict(env or os.environ, PROVEN_TDD_GATE_DIR=self.folder, PROVEN_HOOK_PROFILE="minimal")
        script = FORMAT if mode == "format" else GATE
        args = [sys.executable, script] + ([] if mode == "format" else [mode])
        return subprocess.run(args, input=json.dumps(event), capture_output=True, text=True, encoding="utf-8",
                              env=environment)

    def edit(self, path, tool="Edit"):
        tool_input = {"file_path": path, "old_string": "a", "new_string": "b"}
        return self.send("format", {"hook_event_name": "PostToolUse", "tool_name": tool, "tool_input": tool_input,
                                    "tool_response": {"filePath": path}})

    def done(self, active=False):
        return self.send("done", {"hook_event_name": "Stop", "stop_hook_active": active})


class DoneGate(unittest.TestCase):
    def setUp(self):
        self.folder = tempfile.mkdtemp(prefix="done gate ")
        self.s = MainSession(self.folder)

    def tearDown(self):
        shutil.rmtree(self.folder, ignore_errors=True)

    def test_story_a_fix_with_no_test_run_is_sent_back_once_then_the_developer_decides(self):
        # Given the developer asks for a quick fix and the handler is changed with no test run
        self.s.edit(f"{REPO}/src/Visits.Application/CancelVisit.cs")
        # When the turn would end, it is sent back once, saying what to do
        first = self.s.done()
        self.assertEqual(2, first.returncode)
        self.assertIn("C# changed and no test has run since", first.stderr)
        # Then the answer it gives ends the turn, whatever it says: the developer is there to judge it
        self.assertEqual(0, self.s.done(active=True).returncode)
        self.assertEqual(0, self.s.done().returncode)

    def test_story_a_change_whose_tests_ran_green_ends_without_a_word(self):
        self.s.edit(f"{REPO}/src/Visits.Application/CancelVisit.cs")
        self.s.test_run(PASSED, failed=False)
        self.assertEqual(0, self.s.done().returncode)

    def test_story_a_red_run_is_named_and_the_next_edit_asks_again(self):
        self.s.edit(f"{REPO}/src/Visits.Application/CancelVisit.cs")
        self.s.test_run(FAILED, failed=True)
        red = self.s.done()
        self.assertEqual(2, red.returncode)
        self.assertIn("the last `dotnet test` was red", red.stderr)
        self.assertEqual(0, self.s.done().returncode)
        # A later edit is a new change: the gate asks again
        self.s.edit(f"{REPO}/src/Visits.Application/CancelVisit.cs")
        self.assertEqual(2, self.s.done().returncode)

    def test_a_turn_that_changed_no_production_code_is_never_held(self):
        self.assertEqual(0, self.s.done().returncode)
        self.s.edit(f"{REPO}/tests/Visits.Tests/CancelVisitTests.cs")
        self.s.edit(f"{REPO}/README.md")
        self.assertEqual(0, self.s.done().returncode)

    def test_settings_wire_the_gate_for_the_main_session(self):
        settings = json.load(open(os.path.join(HERE, "..", "..", ".claude", "settings.json"), encoding="utf-8"))
        hooks = settings["hooks"]
        commands = lambda event: [(m.get("matcher"), h.get("if"), h["command"], h.get("async", False))
                                  for m in hooks.get(event, []) for h in m["hooks"]]
        self.assertIn(("*", None, "{PYTHON} {HOOKS}/tdd-gate.py done", False), commands("Stop"))
        for event in ("PostToolUse", "PostToolUseFailure"):
            self.assertIn(("Bash", "Bash(dotnet *)", "{PYTHON} {HOOKS}/tdd-gate.py post", False), commands(event))
        self.assertIn(("Edit|Write|MultiEdit", None, "{PYTHON} {HOOKS}/post-edit-format.py", True), commands("PostToolUse"))


OLD_RED = ("Failed Visits.Infrastructure.Tests.OutboxTests.Relay_BrokerDown_Retries [3 s]\n"
           "Failed Visits.Infrastructure.Tests.OutboxTests.Relay_TwoPods_OneSends [1 s]\n"
           "Failed!  - Failed:     2, Passed:    40, Skipped:     0, Total:    42, Duration: 9 s")
NEW_RED = OLD_RED + "\nFailed Visits.Tests.CancelVisitTests.Cancel_AnHourBefore_FreesTheSlot [12 ms]"
XUNIT_RED = ('[xUnit.net 00:00:02.10]     Visits.Tests.SlotTests.Parse_Edge(value: "a b") [FAIL]\n'
             "Test Run Failed.")


class KnownFailures(unittest.TestCase):
    """Tests that were red before the change: the done gate counts a run whose failures are all known as
    green, and names the failures it doesn't know."""

    def setUp(self):
        self.folder = tempfile.mkdtemp(prefix="known failures ")
        self.repo = os.path.join(self.folder, "visits-service")
        os.makedirs(os.path.join(self.repo, ".claude"))
        self.s = MainSession(self.folder, cwd=self.repo)
        self.code = os.path.join(self.repo, "src", "Visits.Application", "CancelVisit.cs")

    def tearDown(self):
        shutil.rmtree(self.folder, ignore_errors=True)

    def test_story_tests_red_on_master_before_the_change_dont_hold_the_turn(self):
        # Given two outbox tests already fail on master, and the session runs the tests before touching anything
        self.s.test_run(OLD_RED, failed=True)
        # When the handler is changed and the same two still fail, nothing else
        self.s.edit(self.code)
        self.s.test_run(OLD_RED, failed=True)
        # Then the turn ends: the change broke nothing
        self.assertEqual(0, self.s.done().returncode)

    def test_story_a_new_failure_on_top_of_the_old_ones_is_named(self):
        self.s.test_run(OLD_RED, failed=True)
        self.s.edit(self.code)
        self.s.test_run(NEW_RED, failed=True)
        held = self.s.done()
        self.assertEqual(2, held.returncode)
        self.assertIn("CancelVisitTests.Cancel_AnHourBefore_FreesTheSlot", held.stderr)
        self.assertNotIn("Relay_BrokerDown_Retries", held.stderr)

    def test_story_the_repos_known_list_covers_a_run_after_the_first_edit(self):
        # Given the developer listed the tests that fail on master, and the first run comes after the edit
        with open(os.path.join(self.repo, ".claude", "known-failing-tests.txt"), "w", encoding="utf-8") as f:
            f.write("# red on master since the broker upgrade\n"
                    "Visits.Infrastructure.Tests.OutboxTests.Relay_BrokerDown_Retries\n"
                    "Visits.Infrastructure.Tests.OutboxTests.Relay_TwoPods_OneSends\n")
        self.s.edit(self.code)
        self.s.test_run(OLD_RED, failed=True)
        self.assertEqual(0, self.s.done().returncode)

    def test_a_red_run_after_the_first_edit_with_no_list_names_every_failure(self):
        self.s.edit(self.code)
        self.s.test_run(OLD_RED, failed=True)
        held = self.s.done()
        self.assertEqual(2, held.returncode)
        self.assertIn("Relay_BrokerDown_Retries", held.stderr)
        self.assertIn("known-failing-tests.txt", held.stderr)

    def test_a_listed_method_covers_each_of_its_theory_cases_in_xunit_output(self):
        with open(os.path.join(self.repo, ".claude", "known-failing-tests.txt"), "w", encoding="utf-8") as f:
            f.write("Visits.Tests.SlotTests.Parse_Edge\n")
        self.s.edit(self.code)
        self.s.test_run(XUNIT_RED, failed=True)
        self.assertEqual(0, self.s.done().returncode)

    def test_a_red_run_after_qa_engineer_wrote_tests_is_not_taken_as_old(self):
        # Given qa-engineer wrote the story's failing tests, and the session runs them before the code
        self.s.send("post", {"hook_event_name": "PostToolUse", "tool_name": "Agent",
                             "tool_input": {"subagent_type": "qa-engineer", "prompt": "write the tests"}})
        self.s.test_run(NEW_RED, failed=True)
        # When the code is written and its test still fails, the turn is held
        self.s.edit(self.code)
        self.s.test_run(NEW_RED, failed=True)
        self.assertEqual(2, self.s.done().returncode)

    def test_a_build_that_doesnt_compile_is_never_known(self):
        self.s.test_run(OLD_RED, failed=True)
        self.s.edit(self.code)
        self.s.test_run(OLD_RED + "\n" + NO_COMPILE, failed=True)
        self.assertEqual(2, self.s.done().returncode)

    def test_a_red_summary_whose_names_were_cut_off_stays_red(self):
        self.s.test_run(OLD_RED, failed=True)
        self.s.edit(self.code)
        self.s.test_run("Failed!  - Failed:     3, Passed:    40, Skipped:     0, Total:    43", failed=True)
        self.assertEqual(2, self.s.done().returncode)


class KnownFailuresInTddGuide(unittest.TestCase):
    """Inside the agent only the repo's list is known: its first red run is often qa-engineer's tests."""

    def setUp(self):
        self.folder = tempfile.mkdtemp(prefix="tdd gate known ")
        self.repo = os.path.join(self.folder, "visits-service")
        os.makedirs(os.path.join(self.repo, ".claude"))
        with open(os.path.join(self.repo, ".claude", "known-failing-tests.txt"), "w", encoding="utf-8") as f:
            f.write("Visits.Infrastructure.Tests.OutboxTests.Relay_BrokerDown_Retries\n"
                    "Visits.Infrastructure.Tests.OutboxTests.Relay_TwoPods_OneSends\n")
        self.s = Session(self.folder, cwd=self.repo)
        self.code = os.path.join(self.repo, "src", "Visits.Application", "CancelVisit.cs")

    def tearDown(self):
        shutil.rmtree(self.folder, ignore_errors=True)

    def test_story_only_listed_failures_never_unlock_the_code(self):
        # Given tdd-guide runs the suite first and only the tests listed as red on master fail
        self.s.test_run(OLD_RED, failed=True)
        # Then that is no failing test for this change: the code still waits
        self.assertEqual(2, self.s.edit(self.code).returncode)

    def test_story_its_new_test_still_red_after_the_code_keeps_it_from_finishing(self):
        self.s.test_run(NEW_RED, failed=True)  # qa-engineer's test fails beside the listed two: unlocked
        self.assertEqual(0, self.s.edit(self.code).returncode)
        self.s.test_run(NEW_RED, failed=True)
        self.assertEqual(2, self.s.stop().returncode)
        self.s.test_run(OLD_RED, failed=True)  # its test passes now; the other two are master's
        self.assertEqual(0, self.s.stop().returncode)


class Production(unittest.TestCase):
    def test_a_windows_session_with_backslash_paths_and_dotnet_exe(self):
        folder = tempfile.mkdtemp(prefix="tdd gate win ")
        try:
            s = Session(folder, cwd="C:\\work\\visits-service")
            self.assertEqual(2, s.edit("C:\\work\\visits-service\\src\\Visits.Application\\CancelVisit.cs").returncode)
            s.send("post", {"hook_event_name": "PostToolUseFailure", "tool_name": "Bash",
                            "tool_input": {"command": "dotnet.exe test tests\\Visits.Tests"}, "error": "Exit code 1\r\n" + FAILED.replace("\n", "\r\n")})
            self.assertEqual(0, s.edit("C:\\work\\visits-service\\src\\Visits.Application\\CancelVisit.cs").returncode)
            # A repo cloned under a folder named tests is still production code inside it
            # A Windows run prints CRLF; the names still match the baseline
            k = MainSession(folder, cwd="C:\\work\\visits-service")
            k.test_run(OLD_RED.replace("\n", "\r\n"), failed=True)
            k.edit("C:\\work\\visits-service\\src\\Visits.Application\\CancelVisit.cs")
            k.test_run(OLD_RED.replace("\n", "\r\n"), failed=True)
            self.assertEqual(0, k.done().returncode)
            t = Session(folder, agent="c1", cwd="C:\\tests\\visits-service")
            self.assertEqual(2, t.edit("C:\\tests\\visits-service\\src\\CancelVisit.cs").returncode)
        finally:
            shutil.rmtree(folder, ignore_errors=True)


if __name__ == "__main__":
    unittest.main()


class ReviewedSession(MainSession):
    """The main session with its transcript, so the gate can read what the answer hands over."""

    def answer(self, text):
        path = os.path.join(self.folder, "transcript.jsonl")
        rows = [{"type": "user", "message": {"role": "user", "content": "fix the review comments and give me the commands"}},
                {"type": "assistant", "message": {"role": "assistant", "content": [{"type": "tool_use", "name": "Bash", "input": {}}]}},
                {"type": "user", "message": {"role": "user", "content": [{"type": "tool_result", "content": "ok"}]}},
                {"type": "assistant", "message": {"role": "assistant", "content": [{"type": "text", "text": text}]}}]
        with open(path, "w", encoding="utf-8") as f:
            f.write("\n".join(json.dumps(r) for r in rows))
        return path

    def reviewed(self, agent="code-reviewer"):
        return self.send("post", {"hook_event_name": "PostToolUse", "tool_name": "Agent",
                                  "tool_input": {"subagent_type": agent, "prompt": "review the diff"}})

    def done_after(self, text, active=False):
        return self.send("done", {"hook_event_name": "Stop", "stop_hook_active": active,
                                  "transcript_path": self.answer(text)})


PUSH = "Run these:\n```powershell\ngit add -A\ngit commit -m \"fix(merit-medals): apply the review\"\ngit push\n```"


class ReviewGate(unittest.TestCase):
    def setUp(self):
        self.folder = tempfile.mkdtemp(prefix="review gate ")
        self.s = ReviewedSession(self.folder)
        self.s.edit(f"{REPO}/src/Visits.Domain/Batch.cs")
        self.s.test_run(PASSED, failed=False)

    def tearDown(self):
        shutil.rmtree(self.folder, ignore_errors=True)

    def test_story_push_commands_for_an_unreviewed_change_are_sent_back_once(self):
        # Given the code changed and its tests are green, but no reviewer looked at it
        # When the answer hands the developer push commands
        first = self.s.done_after(PUSH)
        # Then it is sent back once, to run the review or say why not
        self.assertEqual(2, first.returncode)
        self.assertIn("Review gate", first.stderr)
        self.assertIn("/full-review", first.stderr)
        self.assertEqual(0, self.s.done_after(PUSH).returncode)

    def test_story_a_review_after_the_last_edit_lets_the_handover_through(self):
        self.s.reviewed()
        self.assertEqual(0, self.s.done_after(PUSH).returncode)

    def test_an_mr_description_counts_as_a_handover(self):
        self.assertEqual(2, self.s.done_after("Here is the MR description for the base:\n\n## What changed").returncode)

    def test_an_answer_that_hands_nothing_over_is_not_held(self):
        self.assertEqual(0, self.s.done_after("The tests pass. The entity now checks its moves first.").returncode)

    def test_an_edit_after_the_review_needs_another(self):
        self.s.reviewed()
        self.s.edit(f"{REPO}/src/Visits.Domain/Batch.cs")
        self.s.test_run(PASSED, failed=False)
        self.assertEqual(2, self.s.done_after(PUSH).returncode)

    def test_another_agent_does_not_count_as_a_review(self):
        self.s.reviewed(agent="Explore")
        self.assertEqual(2, self.s.done_after(PUSH).returncode)

    def test_a_subagents_edit_still_needs_a_review_before_the_handover(self):
        helper = Session(self.folder)
        helper.send = lambda mode, event, env=None: Session.send(helper, mode, {**event, "session_id": "s-2"}, env)
        self.s.reviewed()
        helper.test_run(FAILED, failed=True)
        helper.edit(f"{REPO}/src/Visits.Domain/Batch.cs")
        self.assertEqual(2, self.s.done_after(PUSH).returncode)
