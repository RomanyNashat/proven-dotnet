"""tradeoff.py on two decisions shaped like real ones (fixture/): appointment reminders by broker push or by
polling, which is close, and a queue choice, which isn't; then the tables a hurried ADR gets wrong."""
import json
import os
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parent
SCRIPT = HERE.parents[1] / ".claude" / "skills" / "problem-analysis" / "scripts" / "tradeoff.py"
FIXTURE = HERE / "fixture"


def run(path, *args, env=None):
    return subprocess.run([sys.executable, str(SCRIPT), str(path), *args], capture_output=True, text=True, encoding="utf-8", env=env)


def scored(path):
    out = run(path, "--json")
    assert out.returncode == 0, out.stdout
    return json.loads(out.stdout)


class Stories(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp(prefix="tradeoff "))

    def tearDown(self):
        shutil.rmtree(self.tmp, ignore_errors=True)

    def table(self, body):
        path = self.tmp / "decision.md"
        path.write_text(body, encoding="utf-8")
        return path

    def test_story_a_win_by_four_points_is_close_and_says_what_would_flip_it(self):
        # Given push beating polling 77.1% to 72.9% on four weighted criteria
        result = scored(FIXTURE / "reminders-delivery.md")
        # Then push wins, but the decision is close: one weight moved by 2 or one score by a point flips it
        self.assertEqual("Broker push", result["winner"])
        self.assertEqual({"Broker push": 77.1, "App polls": 72.9}, result["totals"])
        self.assertEqual("close", result["verdict"])
        on_time = next(m for m in result["weights"] if m["criterion"] == "Reminder on time")
        self.assertEqual((3, "App polls"), (on_time["flips_at"], on_time["to"]))
        self.assertIn({"option": "Broker push", "criterion": "Works on old app versions", "from": 2, "to": 1, "result": "App polls"},
                      result["score_flips"])
        self.assertIn("name in the ADR the fact that would", run(FIXTURE / "reminders-delivery.md").stdout)

    def test_story_a_win_no_weight_can_reverse_is_clear(self):
        result = scored(FIXTURE / "queue-choice.md")
        self.assertEqual(("Kafka", "clear"), (result["winner"], result["verdict"]))
        self.assertTrue(all(m["flips_at"] is None for m in result["weights"]))
        self.assertEqual([], result["score_flips"])

    def test_a_tie_is_close_with_no_winner(self):
        result = scored(self.table("| Criterion | Weight | A | B |\n|---|---|---|---|\n| Speed | 2 | 5 | 3 |\n| Cost | 2 | 3 | 5 |\n"))
        self.assertEqual((None, "close"), (result["winner"], result["verdict"]))
        self.assertTrue(run(self.tmp / "decision.md").stdout.startswith("Winner: a tie (close)"))

    def test_a_table_with_holes_or_scores_out_of_range_is_refused(self):
        out = run(self.table("| Criterion | Weight | A | B |\n|---|---|---|---|\n| Speed | | 5 | 3 |\n| Cost | 12 | 7 | |\n"))
        self.assertEqual(1, out.returncode)
        for problem in ("Speed: no weight", "Cost: weight 12 isn't a whole number from 0 to 10",
                        "Cost, A: score 7 isn't a whole number from 1 to 5", "Cost, B: no score"):
            self.assertIn(problem, out.stdout)

    def test_a_table_without_a_weight_column(self):
        out = run(self.table("| Criterion | A | B |\n|---|---|---|\n| Speed | 5 | 3 |\n"))
        self.assertIn("the header must be: Criterion | Weight", out.stdout)

    def test_only_the_first_table_counts(self):
        path = self.table((FIXTURE / "queue-choice.md").read_text(encoding="utf-8") +
                          "\nScores from the spike:\n\n| Criterion | Weight | Kafka | RabbitMQ |\n|---|---|---|---|\n| Cost | 5 | 1 | 5 |\n")
        self.assertEqual("Kafka", scored(path)["winner"])


class Production(unittest.TestCase):
    def test_an_arabic_table_from_a_windows_editor_on_a_cp1252_console(self):
        tmp = Path(tempfile.mkdtemp(prefix="tradeoff win "))
        try:
            path = tmp / "قرار.md"
            body = "| Criterion | Weight | التذكير الفوري | الاستعلام الدوري |\r\n|---|---|---|---|\r\n| يصل في وقته | 5 | 5 | 3 |\r\n| يعمل على النسخ القديمة | 4 | 2 | 5 |\r\n"
            path.write_bytes("﻿".encode() + body.encode("utf-8"))
            out = subprocess.run([sys.executable, str(SCRIPT), str(path)], capture_output=True, env=dict(os.environ, PYTHONIOENCODING="cp1252"))
            self.assertEqual(0, out.returncode, out.stderr.decode("utf-8", "replace"))
            self.assertIn("Winner: الاستعلام الدوري", out.stdout.decode("utf-8"))
        finally:
            shutil.rmtree(tmp, ignore_errors=True)


if __name__ == "__main__":
    unittest.main()
