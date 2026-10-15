import contextlib
import io
import pathlib
import sys
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / ".claude" / "skills" / "test-coverage" / "scripts"))
import coverage_risk  # noqa: E402

FIXTURE = pathlib.Path(__file__).parent / "fixture" / "coverage.cobertura.xml"


class CoverageRiskTests(unittest.TestCase):
    def ranked(self):
        rows = coverage_risk.methods(FIXTURE)
        return sorted(rows, key=lambda m: coverage_risk.crap(m[2], m[3]), reverse=True)

    def test_complex_untested_method_comes_first(self):
        first = self.ranked()[0]
        self.assertEqual("Clinic.Billing.InvoiceCalculator.Total", first[0])
        self.assertAlmostEqual(12 ** 2 * 0.9 ** 3 + 12, coverage_risk.crap(first[2], first[3]))

    def test_async_state_machine_is_folded_back_into_its_method(self):
        names = [row[0] for row in self.ranked()]
        self.assertIn("Clinic.Booking.SlotService.BookAsync", names)
        self.assertNotIn("MoveNext", " ".join(names))

    def test_untested_simple_getter_scores_only_its_complexity(self):
        getter = next(r for r in self.ranked() if r[0].endswith("get_Name"))
        self.assertEqual(2.0, coverage_risk.crap(getter[2], getter[3]))

    def test_fully_covered_complex_method_scores_its_complexity_and_is_not_flagged(self):
        rules = next(r for r in self.ranked() if r[0].endswith("CanBook"))
        self.assertEqual(8.0, coverage_risk.crap(rules[2], rules[3]))

    def test_constructors_are_left_out(self):
        self.assertFalse(any(".ctor" in r[0] for r in self.ranked()))

    def test_report_flags_only_what_needs_a_test(self):
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            self.assertEqual(0, coverage_risk.main([str(FIXTURE), "--top", "10"]))
        flagged = [line for line in out.getvalue().splitlines() if "test before changing" in line]
        self.assertEqual(2, len(flagged))   # complexity 12 at 10% and 6 at 0%; not 8 at 100%
        self.assertIn("InvoiceCalculator.Total", flagged[0])
        self.assertIn("SlotService.BookAsync", flagged[1])


if __name__ == "__main__":
    unittest.main()
