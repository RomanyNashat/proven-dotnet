"""rule_candidates.py on the business-rules sample (tests/SkillSamples.Tests/BusinessRules: a visit
cancellation whose rules were never written down) and on copies of it changed the way real code changes."""
import os
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / ".claude" / "skills" / "business-rules" / "scripts" / "rule_candidates.py"
TRACE = ROOT / ".claude" / "skills" / "test-design" / "scripts" / "trace_matrix.py"
SAMPLE = ROOT / "tests" / "SkillSamples.Tests" / "BusinessRules"


def run(*args, env=None):
    return subprocess.run([sys.executable, str(SCRIPT), *map(str, args)], capture_output=True, text=True, encoding="utf-8", env=env)


class Stories(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp(prefix="business rules "))
        self.code = self.tmp / "Visits"
        shutil.copytree(SAMPLE, self.code)
        self.rules = self.code / "rules.md"

    def tearDown(self):
        shutil.rmtree(self.tmp, ignore_errors=True)

    def edit(self, name, old, new):
        f = self.code / name
        text = f.read_text(encoding="utf-8")
        self.assertIn(old, text)
        f.write_text(text.replace(old, new, 1), encoding="utf-8")

    def test_story_the_mined_rules_account_for_every_deciding_line(self):
        # Given the sample and the rules mined from it
        result = run(self.code, "--check", self.rules)
        # Then every line that decides something is a rule or a not-rule with a reason
        self.assertEqual(0, result.returncode, result.stdout)
        self.assertIn("accounts for all 9 candidate lines", result.stdout)

    def test_story_a_rule_added_later_without_writing_it_down_is_named(self):
        # Given a later change that adds a rule above the others: clinic-cancelled visits stay as they are
        self.edit("VisitCancellation.cs", "        if (visit.Cancelled)\n",
                  "        if (visit.CancelledByClinic)\n            return new(CancelOutcome.AlreadyCancelled, 0m);\n        if (visit.Cancelled)\n")
        result = run(self.code, "--check", self.rules)
        # Then the new guard is named, and the rules that moved two lines down are still found by their code
        self.assertEqual(1, result.returncode)
        problems = [line for line in result.stdout.splitlines() if line.startswith("- ")]
        self.assertEqual(["- VisitCancellation.cs:27 (guard) `if (visit.CancelledByClinic)`: no rule names it, and it isn't under Not rules"],
                         problems)

    def test_a_rule_whose_code_isnt_at_its_line(self):
        text = self.rules.read_text(encoding="utf-8")
        self.rules.write_text(text.replace("`visit.PatientId != patientId`", "`visit.OwnerId != patientId`")
                              .replace(" (VisitCancellation.cs:27 `if (visit.Cancelled)`)", "")
                              .replace("(VisitCancellation.cs:29 `if (byClinic)`)", "(VisitCancellation.cs:29)"), encoding="utf-8")
        out = run(self.code, "--check", self.rules).stdout
        self.assertIn("R1: VisitCancellation.cs:25 doesn't hold `visit.OwnerId != patientId`, there or within 3 lines", out)
        self.assertIn("R2 names no line of code", out)
        self.assertIn("R3: VisitCancellation.cs:29 needs the code it means, in backticks", out)

    def test_a_brace_line_holds_no_rule(self):
        text = self.rules.read_text(encoding="utf-8")
        self.rules.write_text(text.replace("(VisitCancellation.cs:25 `visit.PatientId != patientId`)", "(VisitCancellation.cs:24 `{`)"),
                              encoding="utf-8")
        self.assertIn("R1: VisitCancellation.cs:24 doesn't hold `{`, there or within 3 lines (that line holds no code)",
                      run(self.code, "--check", self.rules).stdout)

    def test_a_line_past_the_end_and_a_file_that_isnt_there(self):
        text = self.rules.read_text(encoding="utf-8")
        self.rules.write_text(text.replace("(VisitCancellation.cs:29 `if (byClinic)`)", "(VisitCancellation.cs:400 `if (byClinic)`)")
                              .replace("(VisitCancellation.cs:33 `left < Cutoff`)", "(Refunds.cs:3 `left < Cutoff`)"), encoding="utf-8")
        out = run(self.code, "--check", self.rules).stdout
        self.assertIn("R3: VisitCancellation.cs:400 is past the end", out)
        self.assertIn("R4: Refunds.cs isn't in", out)

    def test_validators_throws_and_status_changes_are_candidates_and_comparisons_and_comments_arent(self):
        (self.code / "Booking.cs").write_text(
            "namespace V;\n"
            "public sealed class BookingValidator : AbstractValidator<Booking>\n{\n"
            "    public BookingValidator() => RuleFor(b => b.Reason).MaximumLength(500);\n}\n"
            "public sealed class Booking\n{\n"
            "    public Status Status { get; private set; }\n"
            "    // Status = Status.Done when the clinic closes it\n"
            "    public bool IsDone => Status == Status.Done;\n"
            "    public void Close() => Status = Status.Done;\n"
            "    public void Reopen() => throw new BookingClosedException(Id);\n}\n", encoding="utf-8")
        out = run(self.code).stdout
        self.assertIn("| Booking.cs:4 | validation |", out)
        self.assertIn("| Booking.cs:11 | state change |", out)
        self.assertIn("| Booking.cs:12 | throw |", out)
        self.assertNotIn("Booking.cs:9 ", out)
        self.assertNotIn("Booking.cs:10 ", out)

    def test_the_tests_are_not_code_to_mine(self):
        self.assertNotIn("VisitCancellationRulesTests.cs", run(self.code).stdout)

    def test_story_each_rule_is_traced_to_its_tests(self):
        # The rules file is also what trace_matrix.py reads, with the rule prefix and trait
        out = subprocess.run([sys.executable, str(TRACE), str(self.rules), str(self.code), "--prefix", "R", "--trait", "Rule",
                              "--require-all"], capture_output=True, text=True, encoding="utf-8")
        self.assertEqual(0, out.returncode, out.stdout + out.stderr)
        self.assertEqual(6, out.stdout.count("| covered |"))


class Production(unittest.TestCase):
    def test_a_windows_checkout_with_backslash_refs_and_arabic_in_a_guard(self):
        tmp = Path(tempfile.mkdtemp(prefix="business rules win "))
        try:
            code = tmp / "src"
            (code / "Visits").mkdir(parents=True)
            source = ("namespace V;\r\npublic sealed class Names\r\n{\r\n    public string Check(string name)\r\n    {\r\n"
                      "        if (name == \"اختبار\")\r\n            return \"مرفوض\";\r\n        return name;\r\n    }\r\n}\r\n")
            (code / "Visits" / "Names.cs").write_bytes("﻿".encode() + source.encode("utf-8"))
            rules = tmp / "rules.md"
            rules.write_text("- R1: Given the test name, when it is checked, then it is refused. (Visits\\Names.cs:6 `if (name ==`)\n", encoding="utf-8")
            env = dict(os.environ, PYTHONIOENCODING="cp1252")
            listed = subprocess.run([sys.executable, str(SCRIPT), str(code)], capture_output=True, env=env)
            self.assertEqual(0, listed.returncode, listed.stderr.decode("utf-8", "replace"))
            self.assertIn("اختبار", listed.stdout.decode("utf-8"))
            checked = subprocess.run([sys.executable, str(SCRIPT), str(code), "--check", str(rules)], capture_output=True, env=env)
            self.assertEqual(0, checked.returncode, checked.stdout.decode("utf-8", "replace"))
        finally:
            shutil.rmtree(tmp, ignore_errors=True)


if __name__ == "__main__":
    unittest.main()
