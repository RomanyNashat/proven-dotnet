"""trace_matrix.py, on the test-design skill's real sample (story.md and its xUnit tests) and on the TRX that
`dotnet test --logger trx` wrote for it (fixture/testdesign.trx, paths cleaned)."""
import json
import os
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / ".claude" / "skills" / "test-design" / "scripts" / "trace_matrix.py"
SAMPLE = ROOT / "tests" / "SkillSamples.Tests" / "TestDesign"
TRX = Path(__file__).resolve().parent / "fixture" / "testdesign.trx"
sys.path.insert(0, str(SCRIPT.parent))
import trace_matrix  # noqa: E402


def run(*args):
    return subprocess.run([sys.executable, str(SCRIPT), *map(str, args)], capture_output=True, text=True)


class Stories(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp(prefix="trace matrix "))

    def tearDown(self):
        shutil.rmtree(self.tmp, ignore_errors=True)

    def test_story_every_criterion_of_the_sample_is_proven_by_a_test_that_passed(self):
        # Given the sample's story and the results of its tests
        result = run(SAMPLE / "story.md", SAMPLE, "--trx", TRX, "--require-all", "--json")
        # Then every criterion has a test, and every such test passed
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        rows = {r["criterion"]: r for r in json.loads(result.stdout)["criteria"]}
        self.assertEqual(["AC1", "AC2", "AC3", "AC4", "AC5"], sorted(rows))
        self.assertTrue(all(r["status"] == "proven" for r in rows.values()), rows)
        self.assertIn("SkillSamples.TestDesign.VisitBookingTests.Story_ADependentTurnsEighteenAtMidnightInRiyadh_TheGuardianCantBookTheVisit",
                      rows["AC2"]["tests"])

    def test_story_a_criterion_added_to_the_story_without_a_test_stops_the_build(self):
        # Given the product owner adds a criterion and nobody writes its test
        story = self.tmp / "story.md"
        story.write_text((SAMPLE / "story.md").read_text(encoding="utf-8")
                         + "- AC6: A cancelled visit frees its slot for the next patient.\n", encoding="utf-8")
        # When the matrix is required to be complete
        result = run(story, SAMPLE, "--require-all")
        # Then it fails and names the criterion
        self.assertEqual(1, result.returncode)
        self.assertIn("| AC6: A cancelled visit frees its slot for the next patient. | uncovered | - |", result.stdout)

    def test_story_a_failing_test_leaves_its_criterion_unproven(self):
        trx = self.tmp / "results.trx"
        text = TRX.read_text(encoding="utf-8")
        # The first result of the state-transition theory fails
        marker = 'testName="SkillSamples.TestDesign.VisitBookingTests.CanMove_'
        at = text.index(marker)
        outcome = text.index('outcome="Passed"', at)
        trx.write_text(text[:outcome] + 'outcome="Failed"' + text[outcome + len('outcome="Passed"'):], encoding="utf-8")
        result = run(SAMPLE / "story.md", SAMPLE, "--trx", trx, "--require-all", "--json")
        self.assertEqual(1, result.returncode)
        rows = {r["criterion"]: r["status"] for r in json.loads(result.stdout)["criteria"]}
        self.assertEqual("failing", rows["AC5"])
        self.assertEqual("proven", rows["AC4"])

    def _edge_story(self, with_edge_test: bool) -> tuple[Path, Path]:
        story = self.tmp / "story.md"
        story.write_text("# Cancelling a visit\n\n"
                         "- AC1: A patient can cancel a visit up to two hours before it.\n"
                         "- EC1: AC1, boundary: cancelled exactly two hours before, it is allowed\n"
                         "- EC2: AC1, boundary: cancelled one minute later, it is refused\n", encoding="utf-8")
        tests = self.tmp / "Visits.Tests"
        tests.mkdir()
        edge = ('    [Fact, Trait("AC", "AC1"), Trait("EC", "EC2")]\n'
                '    public void Cancel_OneMinuteInsideTheWindow_IsRefused() { }\n') if with_edge_test else ""
        (tests / "CancelTests.cs").write_text(
            "namespace Visits.Tests;\n\npublic sealed class CancelTests\n{\n"
            '    [Fact, Trait("AC", "AC1"), Trait("EC", "EC1")]\n'
            "    public void Cancel_ExactlyTwoHoursBefore_IsAllowed() { }\n" + edge + "}\n", encoding="utf-8")
        return story, tests

    def test_story_a_criterion_covered_by_its_main_path_still_fails_while_an_edge_has_no_test(self):
        # Given a criterion whose main-path test exists, and two edges designed for it, one with no test
        story, tests = self._edge_story(with_edge_test=False)
        # When the matrix is required to be complete
        result = run(story, tests, "--require-all", "--json")
        # Then the criterion shows covered, the edge without a test shows uncovered, and the build stops
        self.assertEqual(1, result.returncode)
        rows = {r["criterion"]: r for r in json.loads(result.stdout)["criteria"]}
        self.assertEqual("covered", rows["AC1"]["status"])
        self.assertEqual("covered", rows["EC1"]["status"])
        self.assertEqual(("uncovered", "AC1"), (rows["EC2"]["status"], rows["EC2"]["of"]))

    def test_story_once_every_edge_has_its_test_the_matrix_passes(self):
        story, tests = self._edge_story(with_edge_test=True)
        self.assertEqual(0, run(story, tests, "--require-all").returncode)

    def test_an_edge_that_names_no_criterion_of_the_story_fails_the_build(self):
        story, tests = self._edge_story(with_edge_test=True)
        story.write_text(story.read_text(encoding="utf-8").replace("EC2: AC1,", "EC2: AC7,"), encoding="utf-8")
        result = run(story, tests, "--require-all")
        self.assertEqual(1, result.returncode)
        self.assertIn("EC2 doesn't start with a criterion the story has", result.stdout)

    def _abuse(self, lines: str, with_test: bool) -> tuple[Path, Path, Path]:
        story, tests = self._edge_story(with_edge_test=True)
        abuse = self.tmp / "abuse.md"
        abuse.write_text("# Abuse cases\n\n" + lines, encoding="utf-8")
        if with_test:
            self._abuse_test(tests)
        return story, tests, abuse

    @staticmethod
    def _abuse_test(tests: Path) -> None:
        (tests / "CancelAbuseTests.cs").write_text(
            "namespace Visits.Tests;\n\npublic sealed class CancelAbuseTests\n{\n"
            '    [Fact, Trait("AB", "AB1")]\n'
            "    public void Cancel_AnotherPatientsVisitById_IsNotFound() { }\n}\n", encoding="utf-8")

    def test_story_a_thief_case_with_no_test_keeps_the_matrix_from_passing(self):
        # Given qa-engineer's story is all proven, and adversary wrote a thief case beside it with no test yet
        story, tests, abuse = self._abuse("- AB1: thief: cancels another patient's visit by changing the id\n", with_test=False)
        # When both files are traced together
        result = run(story, tests, "--also", abuse, "--require-all", "--json")
        # Then the abuse case is a row like any criterion, uncovered, and the build stops
        self.assertEqual(1, result.returncode)
        rows = {r["criterion"]: r for r in json.loads(result.stdout)["criteria"]}
        self.assertEqual(("uncovered", "thief"), (rows["AB1"]["status"], rows["AB1"]["abuser"]))
        # And once its test is written, the matrix passes
        self._abuse_test(tests)
        self.assertEqual(0, run(story, tests, "--also", abuse, "--require-all").returncode)

    def test_an_abuse_case_that_doesnt_say_who_abuses_it_fails_the_build(self):
        story, tests, abuse = self._abuse("- AB1: cancels another patient's visit by changing the id\n", with_test=True)
        result = run(story, tests, "--also", abuse, "--require-all")
        self.assertEqual(1, result.returncode)
        self.assertIn("AB1 doesn't say who abuses it", result.stdout)

    def test_also_with_a_missing_file_is_an_error(self):
        story, tests = self._edge_story(with_edge_test=True)
        result = run(story, tests, "--also", self.tmp / "abuse.md")
        self.assertEqual(2, result.returncode)
        self.assertIn("no such file", result.stderr)

    def test_trait_with_two_prefixes_is_refused(self):
        result = run(SAMPLE / "story.md", SAMPLE, "--prefix", "AC,EC", "--trait", "AC")
        self.assertEqual(2, result.returncode)
        self.assertIn("--trait names one trait", result.stderr)

    def test_a_test_that_names_a_criterion_the_story_lacks_is_listed(self):
        tests = self.tmp / "Tests"
        tests.mkdir()
        (tests / "T.cs").write_text('namespace N;\npublic sealed class T\n{\n    [Fact, Trait("AC", "AC9")]\n'
                                    '    public void A() { }\n}\n', encoding="utf-8")
        story = self.tmp / "s.md"
        story.write_text("AC1: something\n", encoding="utf-8")
        out = run(story, tests).stdout
        self.assertIn("Tests name AC9, which the story doesn't have: N.T.A", out)

    def test_a_story_with_no_criteria_is_an_error_not_an_empty_pass(self):
        story = self.tmp / "s.md"
        story.write_text("As a patient I book a visit.\n", encoding="utf-8")
        self.assertEqual(2, run(story, SAMPLE, "--require-all").returncode)


class Production(unittest.TestCase):
    """The shapes a Windows machine and real repos produce."""

    def test_crlf_bom_class_traits_and_attributes_on_one_line(self):
        tmp = Path(tempfile.mkdtemp(prefix="trace prod "))
        try:
            story = tmp / "story.md"
            story.write_bytes("﻿## Acceptance criteria\r\n1. **AC1**: Arabic names are kept\r\n- AC2 - the old app sees no new field\r\n".encode("utf-8"))
            tests = tmp / "Tests"
            (tests / "bin").mkdir(parents=True)
            (tests / "bin" / "Old.cs").write_text('[Trait("AC", "AC1")]\npublic void Ignored() { }\n', encoding="utf-8")
            (tests / "NameTests.cs").write_bytes((
                "﻿namespace Clinic.Tests;\r\n\r\n[Trait(\"AC\", \"AC1\")]\r\npublic sealed class NameTests\r\n{\r\n"
                "    [Fact] public void Save_ArabicName_IsKept() { }\r\n"
                "    [Theory, InlineData(1), Trait(\"AC\", \"AC2\")] public async Task Get_OldAppVersion_HasNoNewField(int v) { }\r\n"
                "}\r\n").encode("utf-8"))
            result = trace_matrix.matrix(story, tests, None)
            rows = {r["criterion"]: r for r in result["criteria"]}
            self.assertEqual(["Clinic.Tests.NameTests.Get_OldAppVersion_HasNoNewField", "Clinic.Tests.NameTests.Save_ArabicName_IsKept"],
                             rows["AC1"]["tests"])
            self.assertEqual(["Clinic.Tests.NameTests.Get_OldAppVersion_HasNoNewField"], rows["AC2"]["tests"])
            self.assertEqual("the old app sees no new field", rows["AC2"]["text"])
        finally:
            shutil.rmtree(tmp, ignore_errors=True)

    def test_story_a_test_class_with_no_namespace_is_proven_by_its_results(self):
        # Given a test file with no namespace (top-level, as a quick test project often starts)
        tmp = Path(tempfile.mkdtemp(prefix="trace global "))
        try:
            (tmp / "story.md").write_text("- AC1: A patient can cancel up to an hour before\n", encoding="utf-8")
            (tmp / "CancelTests.cs").write_text('public sealed class CancelTests\n{\n    [Fact, Trait("AC", "AC1")]\n'
                                               "    public void Cancel_AnHourBefore_IsAllowed() { }\n}\n", encoding="utf-8")
            (tmp / "results.trx").write_text(
                '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results>'
                '<UnitTestResult testId="a" outcome="Passed" /></Results><TestDefinitions><UnitTest id="a">'
                '<TestMethod className="CancelTests" name="Cancel_AnHourBefore_IsAllowed" /></UnitTest></TestDefinitions></TestRun>',
                encoding="utf-8")
            row = trace_matrix.matrix(tmp / "story.md", tmp, tmp / "results.trx")["criteria"][0]
            # Then its name has no leading dot, so the result matches it and the criterion is proven
            self.assertEqual((["CancelTests.Cancel_AnHourBefore_IsAllowed"], "proven"), (row["tests"], row["status"]))
        finally:
            shutil.rmtree(tmp, ignore_errors=True)

    def test_runs_with_a_bare_environment(self):
        bare = {"PATH": os.environ.get("PATH", ""), "LC_ALL": "C"}
        if "SYSTEMROOT" in os.environ:  # Windows can't start Python without it
            bare["SYSTEMROOT"] = os.environ["SYSTEMROOT"]
        result = subprocess.run([sys.executable, str(SCRIPT), str(SAMPLE / "story.md"), str(SAMPLE)],
                                capture_output=True, text=True, env=bare)
        self.assertEqual(0, result.returncode, result.stderr)

    def test_an_arabic_criterion_printed_to_a_windows_console(self):
        # A story pasted from Jira in Arabic; the developer runs the script in PowerShell, whose console is cp1252
        tmp = Path(tempfile.mkdtemp(prefix="trace arabic "))
        try:
            (tmp / "story.md").write_text("- AC1: يمكن للمريض إلغاء موعده قبل ساعتين.\n", encoding="utf-8")
            (tmp / "Tests").mkdir()
            (tmp / "Tests" / "T.cs").write_text('namespace N;\npublic sealed class T\n{\n    [Fact, Trait("AC", "AC1")]\n'
                                                '    public void Cancel_TwoHoursBefore_IsAllowed() { }\n}\n', encoding="utf-8")
            result = subprocess.run([sys.executable, str(SCRIPT), str(tmp / "story.md"), str(tmp / "Tests")], capture_output=True,
                                    env=dict(os.environ, PYTHONIOENCODING="cp1252"))
            self.assertEqual(0, result.returncode, result.stderr.decode("utf-8", "replace"))
            self.assertIn("يمكن للمريض إلغاء موعده", result.stdout.decode("utf-8"))
        finally:
            shutil.rmtree(tmp, ignore_errors=True)


if __name__ == "__main__":
    unittest.main()
