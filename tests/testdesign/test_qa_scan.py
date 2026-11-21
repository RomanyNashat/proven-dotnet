"""The /qa-scan scripts: changed_members.py on throwaway git repos, and qa_verdict.py on what the real
trace_matrix.py and mutation_report.py print for the committed samples."""
import json
import os
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPTS = ROOT / ".claude" / "skills" / "test-design" / "scripts"
MEMBERS, VERDICT, TRACE = SCRIPTS / "changed_members.py", SCRIPTS / "qa_verdict.py", SCRIPTS / "trace_matrix.py"
REPORT = ROOT / ".claude" / "skills" / "mutation-testing" / "scripts" / "mutation_report.py"
SAMPLE = ROOT / "tests" / "SkillSamples.Tests" / "TestDesign"
TRX = ROOT / "tests" / "testdesign" / "fixture" / "testdesign.trx"
WEAK = ROOT / "tests" / "mutation" / "fixture" / "weak-report.json"

DESK = """namespace Clinic;

public sealed class Desk(IStore store)
{
    private readonly object _gate = new();

    public bool Request(int patient, int prescription)
    {
        if (prescription <= 0)
            return false;
        return store.Add(patient, prescription);
    }

    public int Count => store.Count;

    public bool Move(int request, int to)
    {
        return store.Move(request, to);
    }
}
"""


def git(repo, *args):
    env = dict(os.environ, GIT_AUTHOR_NAME="t", GIT_AUTHOR_EMAIL="t@example.test", GIT_COMMITTER_NAME="t",
               GIT_COMMITTER_EMAIL="t@example.test", GIT_CONFIG_GLOBAL=os.devnull, GIT_CONFIG_NOSYSTEM="1")
    subprocess.run(["git", *args], cwd=repo, env=env, check=True, capture_output=True)


def run(script, *args, cwd=None):
    return subprocess.run([sys.executable, str(script), *map(str, args)], capture_output=True, text=True, cwd=cwd)


class ChangedMembers(unittest.TestCase):
    def setUp(self):
        self.repo = Path(tempfile.mkdtemp(prefix="qa scan "))
        (self.repo / "src" / "Clinic").mkdir(parents=True)
        (self.repo / "tests" / "Clinic.Tests").mkdir(parents=True)
        (self.repo / "src" / "Clinic" / "Desk.cs").write_text(DESK, encoding="utf-8")
        (self.repo / "tests" / "Clinic.Tests" / "DeskTests.cs").write_text("namespace T;\npublic class DeskTests { }\n", encoding="utf-8")
        git(self.repo, "init", "-q", "-b", "main")
        git(self.repo, "add", ".")
        git(self.repo, "commit", "-q", "-m", "start")

    def tearDown(self):
        shutil.rmtree(self.repo, ignore_errors=True)

    def change(self, old, new, path="src/Clinic/Desk.cs"):
        f = self.repo / path
        f.write_text(f.read_text(encoding="utf-8").replace(old, new), encoding="utf-8")

    def members(self):
        out = run(MEMBERS, "--since", "main", "--repo", self.repo, "--json")
        self.assertEqual(0, out.returncode, out.stderr)
        return {(m["file"], m["member"]): m["lines"] for m in json.loads(out.stdout)}

    def test_story_a_fix_inside_a_method_names_that_method_to_retest(self):
        # Given a branch that changes one condition in Request
        self.change("if (prescription <= 0)", "if (prescription < 1)")
        # Then Request is the member to retest, and only it
        self.assertEqual({("src/Clinic/Desk.cs", "Request"): [9]}, self.members())

    def test_story_a_changed_signature_belongs_to_its_own_member_not_the_one_above(self):
        self.change("public bool Move(int request, int to)", "public bool Move(int request, int to, int by)")
        self.assertEqual({("src/Clinic/Desk.cs", "Move"): [16]}, self.members())

    def test_a_property_and_a_test_change(self):
        self.change("public int Count => store.Count;", "public int Count => store.Count + 0;")
        self.change("public class DeskTests { }", "public class DeskTests { int x; }", "tests/Clinic.Tests/DeskTests.cs")
        self.assertEqual({("src/Clinic/Desk.cs", "Count"): [14]}, self.members())

    def test_a_field_change_is_listed_under_its_type(self):
        self.change("private readonly object _gate = new();", "private readonly object _gate = new object();")
        self.assertEqual({("src/Clinic/Desk.cs", "Desk"): [5]}, self.members())

    def test_production_crlf_and_a_bom(self):
        f = self.repo / "src" / "Clinic" / "Desk.cs"
        f.write_bytes("﻿".encode() + DESK.replace("\n", "\r\n").replace("if (prescription <= 0)", "if (prescription < 1)").encode())
        self.assertIn(("src/Clinic/Desk.cs", "Request"), self.members())


class Verdict(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp(prefix="qa verdict "))
        trace = run(TRACE, SAMPLE / "story.md", SAMPLE, "--trx", TRX, "--json")
        (self.tmp / "trace.json").write_text(trace.stdout, encoding="utf-8")
        report = self.tmp / "weak.json"
        report.write_text(WEAK.read_text(encoding="utf-8").replace("ROOT", self.tmp.as_posix()), encoding="utf-8")
        (self.tmp / "mutation.json").write_text(run(REPORT, report, "--json").stdout, encoding="utf-8")

    def tearDown(self):
        shutil.rmtree(self.tmp, ignore_errors=True)

    def verdict(self, *args):
        return run(VERDICT, "--trace", self.tmp / "trace.json", *args)

    def test_story_every_criterion_proven_but_two_edges_unnoticed_is_a_no(self):
        # Given every criterion proven, and a mutation report with two survivors on the changed lines
        result = self.verdict("--mutation", self.tmp / "mutation.json")
        # Then the verdict is NO, naming each survivor
        self.assertEqual(1, result.returncode, result.stdout)
        self.assertIn("Ship: NO  (criteria proven 5/5; mutation 71.43% on the changed lines)", result.stdout)
        self.assertIn("RefillWindow.cs:16: `refillsLeft <= 0` became `refillsLeft < 0` and no test noticed (Survived)", result.stdout)

    def test_story_survivors_judged_harmless_with_a_reason_let_it_ship(self):
        accept = self.tmp / "accepted.txt"
        accept.write_text("# reviewed 2026-11-15\nRefillWindow.cs:16 equivalent here: refills are never negative\n"
                          "RefillWindow.cs:19 covered by the window test in Clinic.Api.Tests\n", encoding="utf-8")
        result = self.verdict("--mutation", self.tmp / "mutation.json", "--accept", accept)
        self.assertEqual(0, result.returncode, result.stdout)
        self.assertIn("Ship: YES", result.stdout)

    def test_no_mutation_report_is_never_a_yes(self):
        result = self.verdict()
        self.assertEqual(2, result.returncode)
        self.assertIn("Ship: CAN'T SAY", result.stdout)
        self.assertIn("note: the tests' strength wasn't measured", result.stdout)

    def test_a_criterion_without_a_test_is_a_no(self):
        story = self.tmp / "story.md"
        story.write_text((SAMPLE / "story.md").read_text(encoding="utf-8") + "- AC6: A cancelled visit frees its slot.\n", encoding="utf-8")
        trace = run(TRACE, story, SAMPLE, "--trx", TRX, "--json")
        (self.tmp / "trace.json").write_text(trace.stdout, encoding="utf-8")
        result = self.verdict("--mutation", self.tmp / "mutation.json")
        self.assertEqual(1, result.returncode)
        self.assertIn("- AC6 is uncovered", result.stdout)

    def test_story_an_edge_case_with_no_test_keeps_the_branch_from_shipping(self):
        # Given every criterion proven, and an edge designed for AC2 that no test names
        story = self.tmp / "story.md"
        story.write_text((SAMPLE / "story.md").read_text(encoding="utf-8")
                         + "- EC1: AC2, boundary: the visit at exactly 00:00 in Riyadh on the birthday\n", encoding="utf-8")
        trace = run(TRACE, story, SAMPLE, "--trx", TRX, "--json")
        (self.tmp / "trace.json").write_text(trace.stdout, encoding="utf-8")
        accept = self.tmp / "accepted.txt"
        accept.write_text("RefillWindow.cs:16 x\nRefillWindow.cs:19 y\n", encoding="utf-8")
        # When the verdict is asked for
        result = self.verdict("--mutation", self.tmp / "mutation.json", "--accept", accept)
        # Then it is NO, naming the edge
        self.assertEqual(1, result.returncode, result.stdout)
        self.assertIn("- EC1 is uncovered", result.stdout)

    def test_inferred_criteria_are_counted_on_the_verdict_line(self):
        story = self.tmp / "story.md"
        story.write_text((SAMPLE / "story.md").read_text(encoding="utf-8").replace("AC1: ", "AC1: (inferred) ", 1), encoding="utf-8")
        trace = run(TRACE, story, SAMPLE, "--trx", TRX, "--json")
        (self.tmp / "trace.json").write_text(trace.stdout, encoding="utf-8")
        accept = self.tmp / "accepted.txt"
        accept.write_text("RefillWindow.cs:16 x\nRefillWindow.cs:19 y\n", encoding="utf-8")
        result = self.verdict("--mutation", self.tmp / "mutation.json", "--accept", accept)
        self.assertEqual(0, result.returncode, result.stdout)
        self.assertIn("Ship: YES  (criteria proven 5/5; 1 inferred, to confirm;", result.stdout)


if __name__ == "__main__":
    unittest.main()
