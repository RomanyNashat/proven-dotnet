"""Tests for tools/layer.py: a layer never touches what isn't its own. Run: python -m unittest discover tests/installer"""
import contextlib
import io
import json
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools"))
import layer  # noqa: E402


def write(path: Path, text: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8")


def make_layer(root: Path, layer_id: str, version: str, files: dict, settings: dict | None = None) -> Path:
    src = root / f"src-{layer_id}-{version}"
    write(src / "layer.json", json.dumps({"id": layer_id, "version": version}))
    for rel, text in files.items():
        write(src / rel, text)
    if settings is not None:
        write(src / "settings.json", json.dumps(settings))
    return src


def run(*argv) -> str:
    out = io.StringIO()
    with contextlib.redirect_stdout(out):
        layer.main([str(a) for a in argv])
    return out.getvalue()


CORE_FILES = {
    "core.md": "core rules",
    "rules/testing.md": "testing rules",
    "agents/planner.md": "planner",
    "commands/tdd.md": "tdd",
    "skills/api-design/SKILL.md": "api skill",
    "skills/api-design/reference/errors.md": "errors",
    "skills/caching/SKILL.md": "caching skill",
    "hooks/stop.py": "print('stop')",
    "files/statusline.ps1": "statusline",
}
CORE_SETTINGS = {
    "env": {"PROVEN_HOOK_PROFILE": "standard"},
    "hooks": {"Stop": [{"matcher": "*", "hooks": [{"type": "command", "command": "python {HOOKS}/stop.py"}]}]},
    "permissions": {"ask": ["Bash(git push:*)"], "deny": ["Read(./.env)"]},
    "statusLine": {"type": "command", "command": "powershell -File {FILES}/statusline.ps1"},
    "attribution": {"commit": ""},
}


class LayerTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)
        self.claude = self.root / ".claude"
        # the user's own things, which no layer may touch
        write(self.claude / "CLAUDE.md", "my own instructions")
        write(self.claude / "skills/my-skill/SKILL.md", "mine")
        write(self.claude / "agents/my-agent.md", "mine")
        write(self.claude / "rules/my-rule.md", "mine")
        write(self.claude / "settings.json", json.dumps({
            "model": "opus",
            "env": {"MY_VAR": "1"},
            "hooks": {"Stop": [{"matcher": "*", "hooks": [{"type": "command", "command": "python ~/my-hook.py"}]}]},
            "permissions": {"allow": ["Bash(ls:*)"], "deny": ["Read(./secrets/**)"]},
        }))

    def tearDown(self):
        self.tmp.cleanup()

    def settings(self) -> dict:
        return json.loads((self.claude / "settings.json").read_text(encoding="utf-8"))

    def install(self, src: Path) -> str:
        return run("install", src, "--claude-dir", self.claude)

    def assert_user_things_intact(self):
        self.assertEqual("my own instructions", (self.claude / "CLAUDE.md").read_text(encoding="utf-8"))
        for rel in ("skills/my-skill/SKILL.md", "agents/my-agent.md", "rules/my-rule.md"):
            self.assertEqual("mine", (self.claude / rel).read_text(encoding="utf-8"), rel)
        s = self.settings()
        self.assertEqual("opus", s["model"])
        self.assertEqual("1", s["env"]["MY_VAR"])
        self.assertIn("python ~/my-hook.py", json.dumps(s["hooks"]))
        self.assertIn("Bash(ls:*)", s["permissions"]["allow"])
        self.assertIn("Read(./secrets/**)", s["permissions"]["deny"])

    def test_install_puts_files_in_place_and_keeps_the_users_own(self):
        self.install(make_layer(self.root, "proven", "0.1.0", CORE_FILES, CORE_SETTINGS))

        self.assertEqual("core rules", (self.claude / "rules/proven/core.md").read_text(encoding="utf-8"))
        self.assertTrue((self.claude / "rules/proven/testing.md").is_file())
        self.assertTrue((self.claude / "skills/api-design/reference/errors.md").is_file())
        self.assertTrue((self.claude / "hooks/proven/stop.py").is_file())
        self.assertTrue((self.claude / "proven/statusline.ps1").is_file())
        s = self.settings()
        self.assertEqual("standard", s["env"]["PROVEN_HOOK_PROFILE"])
        self.assertIn("python ~/.claude/hooks/proven/stop.py", json.dumps(s["hooks"]))
        self.assertIn((self.claude / "proven").resolve().as_posix(), s["statusLine"]["command"])
        self.assertEqual({"commit": ""}, s["attribution"])
        self.assertIn("Bash(git push:*)", s["permissions"]["ask"])
        self.assert_user_things_intact()

    def test_installing_twice_changes_nothing(self):
        src = make_layer(self.root, "proven", "0.1.0", CORE_FILES, CORE_SETTINGS)
        self.install(src)
        first = self.settings()
        self.install(src)
        self.assertEqual(first, self.settings())
        self.assertEqual(1, json.dumps(self.settings()["hooks"]).count("hooks/proven/stop.py"))

    def test_update_removes_only_what_the_layer_dropped(self):
        self.install(make_layer(self.root, "proven", "0.1.0", CORE_FILES, CORE_SETTINGS))
        files = {k: v for k, v in CORE_FILES.items() if not k.startswith("skills/caching")}
        files["agents/planner.md"] = "planner v2"
        settings = {k: v for k, v in CORE_SETTINGS.items() if k != "attribution"}
        settings["permissions"] = {"ask": ["Bash(git push:*)"]}  # dropped the deny entry

        self.install(make_layer(self.root, "proven", "0.2.0", files, settings))

        self.assertFalse((self.claude / "skills/caching").exists())
        self.assertEqual("planner v2", (self.claude / "agents/planner.md").read_text(encoding="utf-8"))
        s = self.settings()
        self.assertNotIn("attribution", s)
        self.assertNotIn("Read(./.env)", s["permissions"]["deny"])
        self.assert_user_things_intact()

    def test_a_users_skill_with_the_same_name_is_never_overwritten(self):
        write(self.claude / "skills/api-design/SKILL.md", "my api-design")

        out = self.install(make_layer(self.root, "proven", "0.1.0", CORE_FILES, CORE_SETTINGS))

        self.assertIn("skipped skills/api-design", out)
        self.assertEqual("my api-design", (self.claude / "skills/api-design/SKILL.md").read_text(encoding="utf-8"))
        self.assertFalse((self.claude / "skills/api-design/reference").exists())
        run("uninstall", "proven", "--claude-dir", self.claude)
        self.assertEqual("my api-design", (self.claude / "skills/api-design/SKILL.md").read_text(encoding="utf-8"))

    def test_a_setting_the_user_already_has_is_kept(self):
        s = self.settings()
        s["statusLine"] = {"type": "command", "command": "my-statusline"}
        (self.claude / "settings.json").write_text(json.dumps(s), encoding="utf-8")

        out = self.install(make_layer(self.root, "proven", "0.1.0", CORE_FILES, CORE_SETTINGS))
        run("uninstall", "proven", "--claude-dir", self.claude)

        self.assertIn("kept your statusLine", out)
        self.assertEqual("my-statusline", self.settings()["statusLine"]["command"])

    def test_a_value_the_user_changed_after_install_is_left_alone(self):
        self.install(make_layer(self.root, "proven", "0.1.0", CORE_FILES, CORE_SETTINGS))
        s = self.settings()
        s["env"]["PROVEN_HOOK_PROFILE"] = "minimal"
        (self.claude / "settings.json").write_text(json.dumps(s), encoding="utf-8")

        self.install(make_layer(self.root, "proven", "0.2.0", CORE_FILES, CORE_SETTINGS))
        run("uninstall", "proven", "--claude-dir", self.claude)

        self.assertEqual("minimal", self.settings()["env"]["PROVEN_HOOK_PROFILE"])

    def test_a_permission_the_user_removed_is_not_added_back(self):
        self.install(make_layer(self.root, "proven", "0.1.0", CORE_FILES, CORE_SETTINGS))
        s = self.settings()
        s["permissions"]["ask"].remove("Bash(git push:*)")
        (self.claude / "settings.json").write_text(json.dumps(s), encoding="utf-8")

        self.install(make_layer(self.root, "proven", "0.2.0", CORE_FILES, CORE_SETTINGS))

        self.assertNotIn("Bash(git push:*)", self.settings().get("permissions", {}).get("ask", []))

    def test_two_layers_side_by_side(self):
        core = make_layer(self.root, "proven", "0.1.0", CORE_FILES, CORE_SETTINGS)
        company_files = {
            "core.md": "company rules",
            "skills/company-packages/SKILL.md": "company skill",
            "skills/api-design/SKILL.md": "company api-design",   # collides with the core's: must be refused
            "hooks/guard.py": "print('guard')",
        }
        company_settings = {
            "env": {"COMPANY_FEED": "internal"},
            "hooks": {"Stop": [{"matcher": "*", "hooks": [{"type": "command", "command": "python {HOOKS}/guard.py"}]}]},
            "permissions": {"ask": ["Bash(git commit:*)", "Bash(git push:*)"]},
            "attribution": {"commit": "", "pr": ""},
        }
        self.install(core)
        out = self.install(make_layer(self.root, "proven-company", "0.1.0", company_files, company_settings))

        self.assertIn("skipped skills/api-design: belongs to the layer 'proven'", out)
        self.assertIn("attribution is set by another layer", out)
        self.assertEqual("company rules", (self.claude / "rules/proven-company/core.md").read_text(encoding="utf-8"))
        self.assertEqual("core rules", (self.claude / "rules/proven/core.md").read_text(encoding="utf-8"))

        # updating the core alone leaves the company layer as it is
        self.install(make_layer(self.root, "proven", "0.2.0", CORE_FILES, CORE_SETTINGS))
        self.assertTrue((self.claude / "skills/company-packages/SKILL.md").is_file())
        hooks = json.dumps(self.settings()["hooks"])
        self.assertIn("hooks/proven-company/guard.py", hooks)
        self.assertEqual(1, hooks.count("hooks/proven/stop.py"))

        # removing the company layer leaves the core and the user's things
        run("uninstall", "proven-company", "--claude-dir", self.claude)
        s = self.settings()
        self.assertFalse((self.claude / "rules/proven-company").exists())
        self.assertNotIn("COMPANY_FEED", s["env"])
        self.assertNotIn("guard.py", json.dumps(s["hooks"]))
        self.assertIn("Bash(git push:*)", s["permissions"]["ask"])   # still the core's
        self.assertNotIn("Bash(git commit:*)", s["permissions"]["ask"])
        self.assertTrue((self.claude / "rules/proven/core.md").is_file())
        self.assert_user_things_intact()

    def test_uninstall_leaves_exactly_the_users_things(self):
        before = json.loads((self.claude / "settings.json").read_text(encoding="utf-8"))
        self.install(make_layer(self.root, "proven", "0.1.0", CORE_FILES, CORE_SETTINGS))

        run("uninstall", "proven", "--claude-dir", self.claude)

        self.assertEqual(before, self.settings())
        left = sorted(p.relative_to(self.claude).as_posix() for p in self.claude.rglob("*")
                      if p.is_file() and ".layers" not in p.parts)
        self.assertEqual(["CLAUDE.md", "agents/my-agent.md", "rules/my-rule.md", "settings.json",
                          "skills/my-skill/SKILL.md"], left)

    def test_invalid_settings_json_stops_without_changing_anything(self):
        (self.claude / "settings.json").write_text("{ not json", encoding="utf-8")
        with self.assertRaises(SystemExit):
            self.install(make_layer(self.root, "proven", "0.1.0", CORE_FILES, CORE_SETTINGS))
        self.assertEqual("{ not json", (self.claude / "settings.json").read_text(encoding="utf-8"))


if __name__ == "__main__":
    unittest.main()
