"""The eval method's pilot and noise floor: run-queue.sh --pilot and gate.py noise.

The queue is run with stand-ins for run-round.sh and resume-run.sh, so no model is called: each stand-in
writes the run's report and transcript and records that it ran.
"""
import os
import shutil
import subprocess
import sys
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import gate  # noqa: E402

STAND_IN = """#!/usr/bin/env bash
# run-round.sh stand-in: <fixture> <arm> <ref> <run folder>
mkdir -p "$4/work"; echo "report" > "$4/work/report.md"; echo '{}' > "$4/transcript.jsonl"
echo "$(basename "$4")" >> "$(dirname "$4")/ran.txt"
"""


class PilotStories(unittest.TestCase):
    def setUp(self):
        self.tools = tempfile.mkdtemp(prefix="eval tools ")
        self.rounds = tempfile.mkdtemp(prefix="eval rounds ")
        shutil.copy(os.path.join(HERE, "run-queue.sh"), self.tools)
        for name in ("run-round.sh", "resume-run.sh"):
            with open(os.path.join(self.tools, name), "w") as f:
                f.write(STAND_IN)

    def tearDown(self):
        shutil.rmtree(self.tools, ignore_errors=True)
        shutil.rmtree(self.rounds, ignore_errors=True)

    def queue(self, *specs):
        return subprocess.run(["bash", os.path.join(self.tools, "run-queue.sh"), "fix-x", self.rounds, *specs],
                              capture_output=True, text=True)

    def ran(self):
        path = os.path.join(self.rounds, "ran.txt")
        return open(path).read().split() if os.path.exists(path) else []

    def test_story_a_pilot_runs_only_the_baseline_before_the_grid_is_paid_for(self):
        # Given a grid of four runs with the baseline first
        grid = ["rules-1=rules-only@main", "proven-1=with-proven@main", "rules-2=rules-only@main", "proven-2=with-proven@main"]
        # When the queue is started as a pilot
        result = self.queue("--pilot", *grid)
        # Then only the baseline ran, and the queue stops to have it graded
        self.assertEqual(4, result.returncode, result.stdout)
        self.assertEqual(["rules-1"], self.ran())
        self.assertIn("pilot: rules-1 finished; grade it against the bar", result.stdout)

    def test_story_after_the_pilot_the_grid_carries_on_without_rerunning_it(self):
        grid = ["rules-1=rules-only@main", "proven-1=with-proven@main"]
        self.queue("--pilot", *grid)
        # When the full queue runs next, the pilot's run is already done
        result = self.queue(*grid)
        self.assertEqual(0, result.returncode, result.stdout)
        self.assertEqual(["rules-1", "proven-1"], self.ran())
        self.assertIn("rules-1: done", result.stdout)
        self.assertIn("queue finished", result.stdout)


class NoiseFloor(unittest.TestCase):
    def test_story_two_identical_arms_set_the_floor_a_change_must_clear(self):
        # Given the same harness run twice, three runs each, on one fixture
        scores = {"arms": {
            "proven-a": {"runs": 3, "built": 3, "passes": {"F1": 3, "F2": 2, "R1": 3}},
            "proven-b": {"runs": 3, "built": 3, "passes": {"F1": 3, "F2": 3, "R1": 3}},
        }}
        # When the noise is read
        differ, floor = gate.noise(scores, "proven-a", "proven-b")
        # Then one case moved by one run in three: anything smaller isn't a shown difference
        self.assertEqual([("F2", 2 / 3, 1.0)], differ)
        self.assertAlmostEqual(1 / 3, floor)

    def test_identical_arms_have_no_noise(self):
        same = {"runs": 2, "built": 2, "passes": {"F1": 2}}
        self.assertEqual(([], 0.0), gate.noise({"arms": {"a": same, "b": dict(same)}}, "a", "b"))

    def test_different_run_counts_compare_as_rates(self):
        scores = {"arms": {"a": {"runs": 4, "built": 4, "passes": {"F1": 2}},
                           "b": {"runs": 2, "built": 2, "passes": {"F1": 1}}}}
        self.assertEqual(([], 0.0), gate.noise(scores, "a", "b"))


if __name__ == "__main__":
    unittest.main()
