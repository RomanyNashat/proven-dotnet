"""bisect_regression.py on a throwaway repo whose history holds a real regression: a refund rule that a
later commit breaks, a commit in between whose build fails, and a developer mid-change on another file."""
import os
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).resolve().parents[2] / ".claude" / "skills" / "testing-tdd" / "scripts" / "bisect_regression.py"

RULE = "def refund(paid, hours_away):\n    return paid if hours_away >= 24 else paid / 2\n"
BROKEN = "def refund(paid, hours_away):\n    return paid if hours_away > 24 else paid / 2\n"
TEST = "from rule import refund\nassert refund(200, 24) == 200, 'a day out gets everything back'\n"


def git(repo, *args):
    env = dict(os.environ, GIT_AUTHOR_NAME="Dev", GIT_AUTHOR_EMAIL="dev@example.test", GIT_COMMITTER_NAME="Dev",
               GIT_COMMITTER_EMAIL="dev@example.test", GIT_CONFIG_GLOBAL=os.devnull, GIT_CONFIG_NOSYSTEM="1")
    return subprocess.run(["git", *args], cwd=repo, env=env, check=True, capture_output=True, text=True).stdout.strip()


class Stories(unittest.TestCase):
    def setUp(self):
        self.repo = Path(tempfile.mkdtemp(prefix="bisect repo "))
        git(self.repo, "init", "-q", "-b", "main")
        self.commit("rule.py", RULE, "add the refund rule", extra={"test_refund.py": TEST, "build.ok": "yes\n"})
        self.good = git(self.repo, "rev-parse", "HEAD")
        self.commit("notes.md", "notes\n", "write the notes")
        self.commit("build.ok", "no\n", "half-done build change")            # the build fails here
        self.commit("build.ok", "yes\n", "finish the build change")
        self.commit("rule.py", BROKEN, "tidy the refund comparison")         # the regression
        self.culprit = git(self.repo, "rev-parse", "--short", "HEAD")
        self.commit("other.md", "x\n", "unrelated docs")
        self.commit("other.md", "y\n", "more unrelated docs")
        # The developer is mid-change on another file, uncommitted
        (self.repo / "notes.md").write_text("half-written thought\n", encoding="utf-8")

    def tearDown(self):
        shutil.rmtree(self.repo, ignore_errors=True)

    def commit(self, name, text, message, extra=None):
        for n, t in {name: text, **(extra or {})}.items():
            (self.repo / n).write_text(t, encoding="utf-8")
        git(self.repo, "add", "-A")
        git(self.repo, "commit", "-q", "-m", message)

    def bisect(self, *extra, good=None):
        build = f'"{sys.executable}" -c "import sys; sys.exit(open(\'build.ok\').read().strip() != \'yes\')"'
        return subprocess.run([sys.executable, str(SCRIPT), "--repo", str(self.repo), "--good", good or self.good, "--build", build,
                               *extra, "--", sys.executable, "test_refund.py"], capture_output=True, text=True, encoding="utf-8")

    def test_story_the_commit_that_broke_the_refund_is_named(self):
        # Given a refund test that passed when the rule was added and fails today
        result = self.bisect()
        # Then the commit that changed >= to > is named, not the half-done build change before it
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertIn(f"First bad commit: {self.culprit} tidy the refund comparison", result.stdout)
        self.assertIn("Files: rule.py", result.stdout)

    def test_story_the_developers_checkout_never_moves(self):
        head, branch = git(self.repo, "rev-parse", "HEAD"), git(self.repo, "branch", "--show-current")
        self.bisect()
        # Their branch, their commit and their uncommitted line are all where they left them
        self.assertEqual((head, "main"), (git(self.repo, "rev-parse", "HEAD"), git(self.repo, "branch", "--show-current")))
        self.assertEqual("half-written thought\n", (self.repo / "notes.md").read_text(encoding="utf-8"))
        self.assertEqual(("notes.md", ""), (git(self.repo, "diff", "--name-only"), git(self.repo, "diff", "--cached", "--name-only")))
        self.assertEqual(1, len(git(self.repo, "worktree", "list").splitlines()))
        self.assertFalse((self.repo / ".git" / "BISECT_LOG").exists())

    def test_story_a_broken_build_right_before_the_break_is_skipped_not_blamed(self):
        # Given the build was broken in the very commit before the regression, so that commit can't be judged
        git(self.repo, "checkout", "-q", "--", "notes.md")   # this story has no change in progress
        git(self.repo, "checkout", "-q", "-b", "tight", self.good)
        self.commit("build.ok", "no\n", "start moving the build")
        broken = git(self.repo, "rev-parse", "--short", "HEAD")
        self.commit("rule.py", BROKEN, "tidy the refund comparison, finish the build", extra={"build.ok": "yes\n"})
        culprit = git(self.repo, "rev-parse", "--short", "HEAD")
        result = self.bisect()
        # Then it doesn't blame either one: it says the break is in one of the two
        self.assertEqual(2, result.returncode, result.stdout)
        self.assertIn(f"- {broken} start moving the build", result.stdout)
        self.assertIn(f"- {culprit} tidy the refund comparison, finish the build", result.stdout)

    def test_production_both_ways_git_writes_its_log(self):
        # git 2.43 writes "first bad commit", 2.56 (CI's) "first 'bad' commit": the result is read either way
        import importlib.util
        spec = importlib.util.spec_from_file_location("bisect_regression", SCRIPT)
        mod = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(mod)
        sha = "a" * 40
        for line in (f"# first bad commit: [{sha}] x", f"# first 'bad' commit: [{sha}] x"):
            self.assertEqual(sha, mod.FIRST_BAD.search(line).group(1))
        for line in (f"# possible first bad commit: [{sha}] x", f"# possible first 'bad' commit: [{sha}] x"):
            self.assertEqual([sha], mod.POSSIBLE_BAD.findall(line))

    def test_a_test_that_passes_today_has_nothing_to_bisect(self):
        result = self.bisect("--bad", self.good)
        self.assertEqual(1, result.returncode)
        self.assertIn("The test passes on", result.stdout)

    def test_a_good_commit_where_the_test_already_fails(self):
        result = self.bisect(good=git(self.repo, "rev-parse", "HEAD~1"))
        self.assertEqual(1, result.returncode)
        self.assertIn("The test doesn't pass on", result.stdout)


if __name__ == "__main__":
    unittest.main()
