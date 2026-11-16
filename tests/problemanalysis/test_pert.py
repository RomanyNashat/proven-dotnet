"""pert.py on a real-shaped plan (fixture/cancel-visit-plan.md: five tasks to let patients cancel a visit,
one of them hiding an unknown), and on the estimates a hurried plan gets wrong."""
import json
import os
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parent
SCRIPT = HERE.parents[1] / ".claude" / "skills" / "problem-analysis" / "scripts" / "pert.py"
PLAN = HERE / "fixture" / "cancel-visit-plan.md"


def run(path, *args, env=None):
    return subprocess.run([sys.executable, str(SCRIPT), str(path), *args], capture_output=True, text=True, encoding="utf-8", env=env)


class Stories(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp(prefix="pert "))

    def tearDown(self):
        shutil.rmtree(self.tmp, ignore_errors=True)

    def plan(self, body):
        path = self.tmp / "plan.md"
        path.write_text(body, encoding="utf-8")
        return path

    def test_story_the_plan_promises_the_80_percent_figure_and_names_what_drives_it(self):
        # Given five tasks, one whose worst case is 14 hours against a likely 4
        result = json.loads(run(PLAN, "--json").stdout)
        # Then the plan expects 20 hours, is done within 22 hours 80% of the time and 23 hours 90%,
        # and one task makes up most of the uncertainty and is flagged for a spike
        self.assertEqual((20.0, {"80%": 22, "90%": 23}), (result["expected"], result["by"]))
        self.assertEqual([{"task": "Free the slot for the waiting list", "share": 73}], result["drivers"])
        slot = next(t for t in result["tasks"] if t["task"] == "Free the slot for the waiting list")
        self.assertEqual((5.3, ["worst is over 3x likely: spike or split it"]), (slot["expected"], slot["flags"]))

    def test_story_the_same_plan_after_the_spike_is_tighter(self):
        # Given the spike read the three callers and the worst case fell from 14 to 6 hours
        text = PLAN.read_text(encoding="utf-8").replace("| 2 | 4 | 14 | WaitingList has three callers we haven't read |",
                                                        "| 3 | 4 | 6 | |")
        result = json.loads(run(self.plan(text), "--json").stdout)
        # Then the 90% figure comes down, and nothing is flagged
        self.assertEqual({"80%": 20, "90%": 21}, result["by"])
        self.assertTrue(all(not t["flags"] for t in result["tasks"]))

    def test_a_task_too_big_to_estimate_and_a_wide_range_with_no_unknown(self):
        out = json.loads(run(self.plan("| Task | Best | Likely | Worst |\n|---|---|---|---|\n| Move the billing service | 16 | 24 | 40 |\n"
                                       "| Report export | 2 | 3 | 8 |\n"), "--json").stdout)
        flags = {t["task"]: t["flags"] for t in out["tasks"]}
        self.assertEqual(["likely is over 16h: split it"], flags["Move the billing service"])
        self.assertEqual(["a wide range with no unknown named"], flags["Report export"])

    def test_estimates_out_of_order_or_missing_are_refused(self):
        out = run(self.plan("| Task | Best | Likely | Worst |\n|---|---|---|---|\n| A | 5 | 3 | 8 |\n| B | 2 | | 6 |\n| C | 1 | 2 | 3 |\n"))
        self.assertEqual(1, out.returncode)
        self.assertIn("A: needs best <= likely <= worst (got 5, 3, 8)", out.stdout)
        self.assertIn("B: best, likely and worst must each be hours (a number)", out.stdout)

    def test_a_header_in_another_order(self):
        out = run(self.plan("| Task | Likely | Best | Worst |\n|---|---|---|---|\n| A | 3 | 2 | 5 |\n"))
        self.assertIn("the header must start: Task | Best | Likely | Worst", out.stdout)

    def test_hours_written_with_an_h_and_halves(self):
        out = json.loads(run(self.plan("| Task | Best | Likely | Worst |\n|---|---|---|---|\n| A | 0.5h | 1h | 2.5h |\n"), "--json").stdout)
        self.assertEqual(1.2, out["expected"])


class Production(unittest.TestCase):
    def test_an_arabic_plan_with_arabic_digits_from_a_windows_editor(self):
        tmp = Path(tempfile.mkdtemp(prefix="pert win "))
        try:
            path = tmp / "خطة.md"
            body = "| Task | Best | Likely | Worst |\r\n|---|---|---|---|\r\n| إلغاء الموعد | ٢ | ٣ | ٥ |\r\n| رسالة للمريض | ١٫٥ | ٢ | ٣ |\r\n"
            path.write_bytes("﻿".encode() + body.encode("utf-8"))
            out = subprocess.run([sys.executable, str(SCRIPT), str(path)], capture_output=True, env=dict(os.environ, PYTHONIOENCODING="cp1252"))
            text = out.stdout.decode("utf-8")
            self.assertEqual(0, out.returncode, out.stderr.decode("utf-8", "replace"))
            self.assertIn("| إلغاء الموعد | 2 | 3 | 5 | 3.2 | - |", text)
            self.assertIn("Expected: 5.3 hours", text)
        finally:
            shutil.rmtree(tmp, ignore_errors=True)


if __name__ == "__main__":
    unittest.main()
