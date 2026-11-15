"""mutation_report.py, on a real Stryker.NET report (fixture/weak-report.json: the mutation sample's weak
tests, paths cleaned to ROOT) placed in a throwaway git repo, so --since sees real diffs."""
import json
import os
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parent
SCRIPT = HERE.parents[1] / ".claude" / "skills" / "mutation-testing" / "scripts" / "mutation_report.py"
SAMPLE = HERE / "Sample" / "Clinic.Rules" / "RefillWindow.cs"
FIXTURE = HERE / "fixture" / "weak-report.json"


def git(repo, *args):
    env = dict(os.environ, GIT_AUTHOR_NAME="t", GIT_AUTHOR_EMAIL="t@example.test", GIT_COMMITTER_NAME="t",
               GIT_COMMITTER_EMAIL="t@example.test", GIT_CONFIG_GLOBAL=os.devnull, GIT_CONFIG_NOSYSTEM="1")
    subprocess.run(["git", *args], cwd=repo, env=env, check=True, capture_output=True)


class Stories(unittest.TestCase):
    def setUp(self):
        self.repo = Path(tempfile.mkdtemp(prefix="mutation repo "))
        (self.repo / "Clinic.Rules").mkdir()
        shutil.copy(SAMPLE, self.repo / "Clinic.Rules" / "RefillWindow.cs")
        git(self.repo, "init", "-q", "-b", "main")
        git(self.repo, "add", ".")
        git(self.repo, "commit", "-q", "-m", "start")
        self.report = self.repo / "mutation-report.json"
        self.report.write_text(FIXTURE.read_text(encoding="utf-8").replace("ROOT", self.repo.as_posix()), encoding="utf-8")

    def tearDown(self):
        shutil.rmtree(self.repo, ignore_errors=True)

    def run_script(self, *args):
        return subprocess.run([sys.executable, str(SCRIPT), str(self.report), "--repo", str(self.repo), *args],
                              capture_output=True, text=True)

    def test_story_full_coverage_but_untested_edges_are_named(self):
        # Given tests that run every line of the rule but only its happy path
        result = self.run_script("--min-score", "90")
        # Then the score is below the bar, and the two edges the tests miss are named where they are
        self.assertEqual(1, result.returncode, result.stdout)
        self.assertIn("Mutation score: 71.43% (5 of 7 changes detected)", result.stdout)
        self.assertIn("| RefillWindow.cs:16 | `refillsLeft <= 0` | `refillsLeft < 0` | Survived |", result.stdout)
        self.assertIn("| RefillWindow.cs:19 | `today >= runsOut.AddDays(-DaysBefore)` | `today > runsOut.AddDays(-DaysBefore)` | Survived |",
                      result.stdout)

    def test_story_a_pull_request_is_judged_on_the_lines_it_changed(self):
        # Given a change that touches only the window's line
        path = self.repo / "Clinic.Rules" / "RefillWindow.cs"
        path.write_text(path.read_text(encoding="utf-8").replace("return today >= runsOut", "return today >= (runsOut)"),
                        encoding="utf-8")
        # When it is judged since main, then only that line's mutants count
        result = json.loads(self.run_script("--since", "main", "--json").stdout)
        self.assertEqual({19}, {m["line"] for m in result["missed"]})
        self.assertEqual(3, result["judged"])

    def test_a_change_with_no_mutants_on_its_lines_passes_and_says_so(self):
        path = self.repo / "Clinic.Rules" / "RefillWindow.cs"
        path.write_text(path.read_text(encoding="utf-8").replace("/// <summary>When", "/// <summary>Exactly when"), encoding="utf-8")
        result = self.run_script("--since", "main", "--min-score", "100")
        self.assertEqual(0, result.returncode)
        self.assertIn("No mutants to judge on the lines changed since main.", result.stdout)


class Production(unittest.TestCase):
    def test_a_windows_report_with_backslashes_and_a_bom(self):
        tmp = Path(tempfile.mkdtemp(prefix="mutation win "))
        try:
            data = json.loads(FIXTURE.read_text(encoding="utf-8").replace("ROOT", "C:/src/clinic"))
            data["projectRoot"] = data["projectRoot"].replace("/", "\\")
            data["files"] = {k.replace("/", "\\"): v for k, v in data["files"].items()}
            report = tmp / "r.json"
            report.write_bytes("\ufeff".encode() + json.dumps(data).encode("utf-8"))
            out = subprocess.run([sys.executable, str(SCRIPT), str(report), "--json"], capture_output=True, text=True)
            result = json.loads(out.stdout)
            self.assertEqual(71.43, result["score"])
            self.assertEqual(2, len(result["missed"]))
        finally:
            shutil.rmtree(tmp, ignore_errors=True)


if __name__ == "__main__":
    unittest.main()
