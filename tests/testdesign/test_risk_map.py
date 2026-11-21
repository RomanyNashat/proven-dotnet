"""risk_map.py on throwaway git repos whose history is written by the test."""
import json
import os
import shutil
import subprocess
import sys
import tempfile
import time
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / ".claude" / "skills" / "test-design" / "scripts" / "risk_map.py"
sys.path.insert(0, str(SCRIPT.parent))
import risk_map  # noqa: E402

DAY = 86400


class Repo:
    def __init__(self, path):
        self.path = path
        self.git("init", "-q")

    def git(self, *args, when=None):
        env = dict(os.environ, GIT_AUTHOR_NAME="t", GIT_AUTHOR_EMAIL="t@example.com",
                   GIT_COMMITTER_NAME="t", GIT_COMMITTER_EMAIL="t@example.com")
        if when is not None:
            env["GIT_AUTHOR_DATE"] = env["GIT_COMMITTER_DATE"] = f"@{int(when)} +0000"
        subprocess.run(["git", "-C", str(self.path), *args], check=True, capture_output=True, env=env)

    def commit(self, subject, *names, days_ago=1):
        for name in names:
            f = self.path / name
            f.parent.mkdir(parents=True, exist_ok=True)
            with f.open("a", encoding="utf-8") as out:
                out.write(subject + "\n")
        self.git("add", "-A")
        self.git("commit", "-q", "-m", subject, when=time.time() - days_ago * DAY)


def run(*args):
    return subprocess.run([sys.executable, str(SCRIPT), *map(str, args)], capture_output=True, text=True, encoding="utf-8")


class RiskMap(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp(prefix="risk map "))
        self.repo = Repo(self.tmp)

    def tearDown(self):
        shutil.rmtree(self.tmp, ignore_errors=True)

    def test_story_the_file_fixed_three_times_this_month_is_where_to_look_first(self):
        # Given booking was fixed three times lately, and the report changed more often but was never fixed
        for n in range(3):
            self.repo.commit(f"fix(booking): slot taken twice #{n}", "src/Booking.cs", days_ago=5 + n)
        for n in range(4):
            self.repo.commit(f"feat(report): add column {n}", "src/Report.cs", days_ago=10 + n)
        # When the map is drawn
        rows = risk_map.rank(self.tmp)
        # Then booking comes first: 3 changes and 3 fixes beat 4 changes and none
        self.assertEqual(["src/Booking.cs", "src/Report.cs"], [r["file"] for r in rows])
        self.assertEqual({"changes": 3, "fixes": 3, "score": 9}, {k: rows[0][k] for k in ("changes", "fixes", "score")})

    def test_story_history_older_than_the_window_doesnt_count(self):
        self.repo.commit("fix: old crash", "src/Old.cs", days_ago=200)
        self.repo.commit("feat: new screen", "src/New.cs", days_ago=3)
        self.assertEqual(["src/New.cs"], [r["file"] for r in risk_map.rank(self.tmp, since="90 days ago")])

    def test_a_tie_goes_to_the_file_changed_most_recently(self):
        self.repo.commit("feat: a", "src/A.cs", days_ago=30)
        self.repo.commit("feat: b", "src/B.cs", days_ago=2)
        self.assertEqual(["src/B.cs", "src/A.cs"], [r["file"] for r in risk_map.rank(self.tmp)])

    def test_tests_other_files_and_deleted_files_are_left_out_by_default(self):
        self.repo.commit("fix: flaky", "tests/Clinic.Tests/BookingTests.cs", "src/Clinic.Tests.Helpers/X.cs", days_ago=2)
        self.repo.commit("fix: typo", "README.md", days_ago=2)
        self.repo.commit("feat: gone soon", "src/Gone.cs", days_ago=3)
        self.repo.git("rm", "-q", "src/Gone.cs")
        self.repo.git("commit", "-q", "-m", "chore: remove gone")
        self.repo.commit("feat: kept", "src/Kept.cs", days_ago=1)
        self.assertEqual(["src/Kept.cs"], [r["file"] for r in risk_map.rank(self.tmp)])
        with_tests = [r["file"] for r in risk_map.rank(self.tmp, with_tests=True)]
        self.assertIn("tests/Clinic.Tests/BookingTests.cs", with_tests)

    def test_what_counts_as_a_fix(self):
        for subject in ("fix: x", "fix(api)!: x", "hotfix: x", "Fixes the null check", "bug in paging", "regression after upgrade"):
            self.assertTrue(risk_map.is_fix(subject), subject)
        for subject in ("feat: prefix handling", "refactor: debug output", "feat: fixture data"):
            self.assertFalse(risk_map.is_fix(subject), subject)

    def test_the_table_and_the_json(self):
        self.repo.commit("fix: slot", "src/Booking.cs", days_ago=4)
        table = run(self.tmp)
        self.assertEqual(0, table.returncode, table.stderr)
        self.assertIn("| src/Booking.cs | 1 | 1 | 4 days ago | 3 |", table.stdout)
        data = json.loads(run(self.tmp, "--json").stdout)
        self.assertEqual("src/Booking.cs", data["files"][0]["file"])

    def test_production_arabic_names_spaces_and_a_folder_that_isnt_a_repo(self):
        # A Windows checkout with spaces and an Arabic file name, and the script pointed at the wrong folder
        self.repo.commit("fix: موعد مكرر", "src/My Service/المواعيد.cs", days_ago=2)
        rows = risk_map.rank(self.tmp)
        self.assertEqual(["src/My Service/المواعيد.cs"], [r["file"] for r in rows])
        elsewhere = Path(tempfile.mkdtemp(prefix="not a repo "))
        try:
            result = run(elsewhere)
            self.assertNotEqual(0, result.returncode)
            self.assertIn("git log failed", result.stderr)
        finally:
            shutil.rmtree(elsewhere, ignore_errors=True)


if __name__ == "__main__":
    unittest.main()
