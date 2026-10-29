import importlib.util
import pathlib
import subprocess
import tempfile
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[3]
spec = importlib.util.spec_from_file_location("changed_since_eval", ROOT / "tools" / "ci" / "changed-since-eval.py")
changed_since_eval = importlib.util.module_from_spec(spec)
spec.loader.exec_module(changed_since_eval)


class ChangedSinceEvalTests(unittest.TestCase):
    def setUp(self):
        self.repo = tempfile.mkdtemp()
        self.git("init", "-q")

    def git(self, *args):
        subprocess.run(["git", "-C", self.repo, "-c", "user.name=t", "-c", "user.email=t@t", *args],
                       check=True, capture_output=True)

    def commit(self, files, message):
        for rel, text in files.items():
            path = pathlib.Path(self.repo, rel)
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(text, encoding="utf-8")
        self.git("add", "-A")
        self.git("commit", "-q", "-m", message)

    def test_story_a_skill_changed_after_the_last_eval_is_listed(self):
        # Given an eval whose scores were committed, then a skill, a rule and a README changed after it
        self.commit({".claude/skills/a/SKILL.md": "v1", "tests/evals/fix-x/scores.json": "{}"}, "round one")
        self.commit({".claude/skills/a/SKILL.md": "v2", ".claude/rules/r.md": "new", "README.md": "x"}, "later")
        # When the report runs
        text = changed_since_eval.report(self.repo)
        # Then it names the eval commit and lists the skill and the rule, not the README
        self.assertIn("### Changed since the last fix eval", text)
        self.assertIn("round one", text)
        self.assertIn("- `M` .claude/skills/a/SKILL.md", text)
        self.assertIn("- `A` .claude/rules/r.md", text)
        self.assertNotIn("README.md", text)

    def test_nothing_changed_since_the_scores_says_so(self):
        self.commit({".claude/agents/x.md": "v1"}, "agent")
        self.commit({"tests/evals/fix-x/scores.json": "{}"}, "scores")
        self.assertIn("No skill, rule, agent or command has changed since.", changed_since_eval.report(self.repo))

    def test_the_last_scores_commit_is_the_base_not_the_first(self):
        self.commit({"tests/evals/fix-x/scores.json": "1", ".claude/commands/c.md": "v1"}, "first")
        self.commit({".claude/commands/c.md": "v2"}, "between")
        self.commit({"tests/evals/fix-y/scores.json": "2"}, "second scores")
        text = changed_since_eval.report(self.repo)
        self.assertIn("second scores", text)
        self.assertNotIn("c.md", text)

    def test_no_scores_yet_and_no_history_never_fail(self):
        self.commit({".claude/skills/a/SKILL.md": "v1"}, "no eval yet")
        self.assertIn("No commit has changed", changed_since_eval.report(self.repo))
        self.assertIn("Couldn't tell", changed_since_eval.report(tempfile.mkdtemp()))


if __name__ == "__main__":
    unittest.main()
