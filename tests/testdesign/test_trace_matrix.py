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
