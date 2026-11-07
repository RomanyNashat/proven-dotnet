import contextlib
import importlib.util
import io
import pathlib
import tempfile
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("budget", ROOT / "tools" / "ci" / "check-listing-budget.py")
budget = importlib.util.module_from_spec(spec)
spec.loader.exec_module(budget)


def claude_dir(skills=(), commands=()):
    """A .claude folder with skills {name: description} and commands {name: (description, hidden)}."""
    base = pathlib.Path(tempfile.mkdtemp()) / ".claude"
    for name, desc in dict(skills).items():
        (base / "skills" / name).mkdir(parents=True)
        (base / "skills" / name / "SKILL.md").write_text(f'---\nname: {name}\ndescription: "{desc}"\n---\nbody\n')
    for name, (desc, hidden) in dict(commands).items():
        (base / "commands").mkdir(parents=True, exist_ok=True)
        flag = "disable-model-invocation: true\n" if hidden else ""
        (base / "commands" / f"{name}.md").write_text(f'---\ndescription: "{desc}"\n{flag}---\nbody\n')
    return base


def run(*args):
    out = io.StringIO()
    with contextlib.redirect_stdout(out):
        code = budget.main(list(args))
    return code, out.getvalue()


class ListingBudgetTests(unittest.TestCase):
    def test_a_short_listing_under_budget_passes(self):
        base = claude_dir({"caching": "Caching for .NET: HybridCache."}, {"tdd": ("Work test-first.", False)})
        code, out = run("--claude-dir", str(base), "--max-chars", "200")
        self.assertEqual(0, code, out)
        self.assertIn("2 entries", out)

    def test_a_command_hidden_from_the_model_isnt_counted(self):
        base = claude_dir({}, {"sleep": ("x" * 500, True), "tdd": ("Work test-first.", False)})
        code, out = run("--claude-dir", str(base), "--max-chars", "100")
        self.assertEqual(0, code, out)
        self.assertIn("1 entries", out)

    def test_a_long_description_is_named(self):
        base = claude_dir({"file-uploads": "y" * 300})
        code, out = run("--claude-dir", str(base), "--max-each", "180")
        self.assertEqual(1, code)
        self.assertIn("file-uploads: description is 300 characters", out)

    def test_story_descriptions_grow_back_past_the_budget_and_ci_says_so(self):
        # Given a core and a team layer whose descriptions are each short but add up past the budget
        core = claude_dir({f"core-{i}": "z" * 150 for i in range(10)})
        team = claude_dir({f"team-{i}": "z" * 150 for i in range(10)})
        # When the team layer counts its listing with the core's
        code, out = run("--claude-dir", str(core), "--claude-dir", str(team), "--max-chars", "2500")
        # Then it fails, saying by how much and what to do
        self.assertEqual(1, code)
        self.assertIn("over budget by", out)
        self.assertIn("disable-model-invocation", out)

    def test_the_repos_own_listing_fits(self):
        code, out = run()
        self.assertEqual(0, code, out)


if __name__ == "__main__":
    unittest.main()
