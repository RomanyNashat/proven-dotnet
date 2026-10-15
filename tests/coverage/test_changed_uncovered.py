import contextlib
import io
import pathlib
import sys
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / ".claude" / "skills" / "test-coverage" / "scripts"))
import changed_uncovered  # noqa: E402

FIXTURE = pathlib.Path(__file__).parent / "fixture"
REPORT = FIXTURE / "coverage.cobertura.xml"
DIFF = FIXTURE / "change.diff"


class ChangedUncoveredTests(unittest.TestCase):
    def rows(self):
        return {row[0]: row for row in changed_uncovered.analyse(DIFF.read_text(encoding="utf-8"), REPORT)}

    def test_only_changed_lines_the_report_measures_count(self):
        # Lines 10 and 11 changed and are measured; the edited comment on 32 isn't a gap.
        path, coverable, covered, gaps = self.rows()["src/Billing/InvoiceCalculator.cs"]
        self.assertEqual((2, 1), (coverable, covered))
        self.assertEqual({"Clinic.Billing.InvoiceCalculator.Total": [11]}, gaps)

    def test_async_gap_is_named_by_its_method_not_the_state_machine(self):
        _, _, _, gaps = self.rows()["src/Booking/SlotService.cs"]
        self.assertEqual({"Clinic.Booking.SlotService.BookAsync": [20]}, gaps)

    def test_a_new_file_no_test_loads_is_reported_on_its_own(self):
        self.assertEqual((None, None), self.rows()["src/Booking/Waitlist.cs"][1:3])

    def test_tests_deleted_files_and_non_code_are_left_out(self):
        paths = set(self.rows())
        self.assertNotIn("tests/Clinic.Tests/SlotRulesTests.cs", paths)
        self.assertNotIn("src/Old/Legacy.cs", paths)
        self.assertNotIn("README.md", paths)

    def test_story_a_merge_request_that_looks_tested_fails_the_gate(self):
        # Given: a change that adds a test file and touches four source files
        # When: the gap check runs with an 80% gate on the changed lines
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            code = changed_uncovered.main([str(DIFF), str(REPORT), "--min", "80"])
        # Then: half the measurable changed lines run in no test, and one new file no test loads, so it fails
        self.assertEqual(1, code)
        self.assertIn("changed lines covered: 2/4 (50%)", out.getvalue())
        self.assertIn("src/Booking/Waitlist.cs: not in the coverage report", out.getvalue())

    def test_a_diff_saved_by_windows_powershell_is_read(self):
        # Windows PowerShell 5.1's `>` writes UTF-16 with a BOM.
        utf16 = FIXTURE / "change-utf16.diff"
        utf16.write_bytes(DIFF.read_text(encoding="utf-8").encode("utf-16"))
        try:
            self.assertEqual(DIFF.read_text(encoding="utf-8"), changed_uncovered.read_text(utf16))
        finally:
            utf16.unlink()

    def test_report_paths_can_be_absolute(self):
        report = FIXTURE / "absolute.cobertura.xml"
        report.write_text(REPORT.read_text(encoding="utf-8").replace('filename="src/', 'filename="/build/repo/src/'), encoding="utf-8")
        try:
            rows = {r[0]: r for r in changed_uncovered.analyse(DIFF.read_text(encoding="utf-8"), report)}
            self.assertEqual((1, 1), rows["src/Booking/SlotRules.cs"][1:3])
        finally:
            report.unlink()


if __name__ == "__main__":
    unittest.main()
