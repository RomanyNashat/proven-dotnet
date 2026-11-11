import contextlib
import importlib.util
import io
import pathlib
import tempfile
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("check_rules", ROOT / "tools" / "ci" / "check-rules.py")
check = importlib.util.module_from_spec(spec)
spec.loader.exec_module(check)


def layout(core, rules, skills=None, inventory=""):
    base = pathlib.Path(tempfile.mkdtemp())
    claude = base / ".claude"
    (claude / "rules").mkdir(parents=True)
    (claude / "core.md").write_text(core)
    for name, text in rules.items():
        (claude / "rules" / name).write_text(text)
    for name, text in (skills or {}).items():
        (claude / "skills" / name).mkdir(parents=True)
        (claude / "skills" / name / "SKILL.md").write_text(text)
    inv = base / "inventory.tsv"
    inv.write_text(inventory)
    return claude, inv


def run(*args):
    out = io.StringIO()
    with contextlib.redirect_stdout(out):
        code = check.main(list(args))
    return code, out.getvalue()


class CheckRulesTests(unittest.TestCase):
    def test_every_rule_where_the_inventory_says_passes(self):
        claude, inv = layout("Never decompile.", {"a.md": "NEVER call .Result"}, {"x": "Span<T> here"},
                             "# comment\ncore.md\tNever decompile\nrules/a.md\tNEVER call .Result\nskills/x/SKILL.md\tSpan<T>\n")
        code, out = run("--claude-dir", str(claude), "--inventory", str(inv), "--max-chars", "1000")
        self.assertEqual(0, code, out)

    def test_story_a_rule_moved_to_a_skill_and_then_dropped_fails_naming_it(self):
        # Given a rule that was moved from the rules into a skill when the rules were cut
        claude, inv = layout("x", {"a.md": "y"}, {"dotnet-core": "Use ArrayPool<T>.Shared."},
                             "skills/dotnet-core/SKILL.md\tArrayPool<T>.Shared\n")
        # When a later edit to the skill drops it
        (claude / "skills" / "dotnet-core" / "SKILL.md").write_text("Nothing about buffers.")
        code, out = run("--claude-dir", str(claude), "--inventory", str(inv))
        # Then the check fails and says which rule and where
        self.assertEqual(1, code)
        self.assertIn("skills/dotnet-core/SKILL.md no longer says 'ArrayPool<T>.Shared'", out)

    def test_the_always_loaded_files_grow_past_the_budget_and_it_says_so(self):
        claude, inv = layout("c" * 600, {"a.md": "r" * 600})
        code, out = run("--claude-dir", str(claude), "--inventory", str(inv), "--max-chars", "1000")
        self.assertEqual(1, code)
        self.assertIn("200 characters over budget", out)

    def test_skills_dont_count_toward_the_budget(self):
        claude, inv = layout("c", {"a.md": "r"}, {"big": "s" * 5000})
        code, out = run("--claude-dir", str(claude), "--inventory", str(inv), "--max-chars", "10")
        self.assertEqual(0, code, out)

    def test_the_repos_own_rules_hold_every_rule_and_fit(self):
        code, out = run()
        self.assertEqual(0, code, out)


if __name__ == "__main__":
    unittest.main()
