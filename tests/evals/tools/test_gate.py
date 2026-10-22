import pathlib
import sys
import unittest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import gate  # noqa: E402


def copy(**results):
    return {"built": True, "results": results}


class GateTests(unittest.TestCase):
    def test_scores_add_up_each_arm_and_skip_the_fixture_copies(self):
        graded = {"original": copy(H01=False), "reference": copy(H01=True),
                  "with-proven-1": copy(H01=True, R1=True), "with-proven-2": copy(H01=False, R1=True)}
        arms = gate.scores(graded)["arms"]
        self.assertEqual(["with-proven"], list(arms))
        self.assertEqual({"H01": 1, "R1": 2}, arms["with-proven"]["passes"])
        self.assertEqual(2, arms["with-proven"]["runs"])

    def test_story_a_lesson_that_raises_the_total_but_drops_one_case_is_refused(self):
        # Given a baseline where with-proven fixes H02 in all three runs and H05 in one
        baseline = {"arms": {"with-proven": {"runs": 3, "passes": {"H02": 3, "H05": 1, "R1": 3}}}}
        # When a rule change from /lessons gets H05 fixed every time but costs H02 a run
        after = {"arms": {"with-proven": {"runs": 3, "passes": {"H02": 2, "H05": 3, "R1": 3}}}}
        failures, better = gate.check(baseline, after)
        # Then the change is refused for H02, though the total went from 7 to 8
        self.assertEqual(["with-proven H02: passed in 2/3 runs, was 3/3"], failures)
        self.assertEqual(["with-proven H05: 1/3 -> 3/3"], better)

    def test_fewer_runs_cant_show_a_case_held(self):
        baseline = {"arms": {"rules-only": {"runs": 3, "passes": {"H01": 3}}}}
        after = {"arms": {"rules-only": {"runs": 1, "passes": {"H01": 1}}}}
        failures, _ = gate.check(baseline, after)
        self.assertEqual(["rules-only: 1 runs, the baseline has 3"], failures)

    def test_more_runs_are_compared_as_rates(self):
        baseline = {"arms": {"a": {"runs": 3, "passes": {"H01": 2}}}}
        self.assertEqual([], gate.check(baseline, {"arms": {"a": {"runs": 6, "passes": {"H01": 4}}}})[0])
        self.assertEqual(1, len(gate.check(baseline, {"arms": {"a": {"runs": 6, "passes": {"H01": 3}}}})[0]))

    def test_a_regression_that_breaks_more_often_is_refused(self):
        baseline = {"arms": {"a": {"runs": 3, "passes": {"R2": 3}}}}
        failures, _ = gate.check(baseline, {"arms": {"a": {"runs": 3, "passes": {"R2": 2}}}})
        self.assertEqual(["a R2: passed in 2/3 runs, was 3/3"], failures)

if __name__ == "__main__":
    unittest.main()
