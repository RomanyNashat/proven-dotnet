"""usage.py: which skills, agents, commands and MCP tools the sessions on this machine used, counted from
Claude Code's local transcripts. Names and counts only: no transcript text is printed."""
import json
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).resolve().parents[2] / ".claude" / "skills" / "retrospective" / "scripts" / "usage.py"


def tool(name, **tool_input):
    return {"type": "assistant", "timestamp": "2026-11-20T10:00:00.000Z",
            "message": {"role": "assistant", "content": [{"type": "tool_use", "id": "t", "name": name, "input": tool_input}]}}


def user(text, at="2026-11-20T10:00:00.000Z"):
    return {"type": "user", "timestamp": at, "message": {"role": "user", "content": text}}


class Usage(unittest.TestCase):
    def setUp(self):
        self.home = Path(tempfile.mkdtemp(prefix="usage "))
        projects = self.home / ".claude" / "projects"
        a = projects / "-work-blood-donation"
        b = projects / "-work-payment"
        a.mkdir(parents=True)
        b.mkdir(parents=True)
        (a / "s1.jsonl").write_text("\n".join(json.dumps(r) for r in [
            user("<command-name>/tdd</command-name> add the cancel rule for patient 1012345678"),
            tool("Skill", skill="testing-tdd"),
            tool("Agent", subagent_type="qa-engineer", prompt="secret prompt text"),
            tool("Agent", subagent_type="tdd-guide", prompt="x"),
            tool("mcp__proven-roslyn__find_callers", symbol="CancelVisit"),
            tool("Bash", command="grep -rn \"CancelVisit\" src --include=*.cs"),
            tool("Bash", command="dotnet test"),
        ]), encoding="utf-8")
        (b / "s2.jsonl").write_text("\n".join(json.dumps(r) for r in [
            user("<command-name>/tdd</command-name>"),
            tool("Skill", skill="payments-aps"),
            tool("Skill", skill="testing-tdd"),
            tool("Agent", subagent_type="qa-engineer", prompt="y"),
            tool("Bash", command="rg -n 'RefundExecutor' --type cs"),
            {"type": "summary", "summary": "not json from a tool"},
            "{broken line",
        ]), encoding="utf-8")
        old = projects / "-work-old"
        old.mkdir()
        (old / "s0.jsonl").write_text(json.dumps(tool("Skill", skill="caching") | {"timestamp": "2026-01-01T00:00:00.000Z"}), encoding="utf-8")
        skills = self.home / ".claude" / "skills"
        for name in ("testing-tdd", "payments-aps", "caching", "nginx"):
            (skills / name).mkdir(parents=True)
            (skills / name / "SKILL.md").write_text(f"---\nname: {name}\n---\n", encoding="utf-8")

    def tearDown(self):
        shutil.rmtree(self.home, ignore_errors=True)

    def run_it(self, *args):
        done = subprocess.run([sys.executable, str(SCRIPT), "--home", str(self.home), "--today", "2026-11-28", *args],
                              capture_output=True, text=True, encoding="utf-8")
        self.assertEqual(0, done.returncode, done.stderr)
        return done.stdout

    def test_story_a_month_of_sessions_counted_by_name_and_session(self):
        rows = json.loads(self.run_it("--days", "30", "--json"))["used"]
        by = {(r["kind"], r["name"]): r for r in rows}
        self.assertEqual((2, 2), (by[("skill", "testing-tdd")]["uses"], by[("skill", "testing-tdd")]["sessions"]))
        self.assertEqual(2, by[("agent", "qa-engineer")]["uses"])
        self.assertEqual(2, by[("command", "/tdd")]["uses"])
        self.assertEqual(1, by[("mcp", "proven-roslyn")]["uses"])
        self.assertNotIn(("skill", "caching"), by)  # used, but before the window

    def test_symbol_searches_by_text_are_counted_beside_the_roslyn_calls(self):
        out = json.loads(self.run_it("--days", "30", "--json"))
        self.assertEqual({"roslyn": 1, "text_search_on_cs": 2}, out["symbol_questions"])

    def test_installed_skills_never_used_in_the_window_are_listed(self):
        out = json.loads(self.run_it("--days", "30", "--json"))
        self.assertEqual(["caching", "nginx"], out["never_used_skills"])

    def test_no_transcript_text_reaches_the_output(self):
        text = self.run_it("--days", "30")
        self.assertNotIn("1012345678", text)
        self.assertNotIn("secret prompt", text)
        self.assertNotIn("CancelVisit", text)
        self.assertIn("testing-tdd", text)

    def test_no_transcripts_at_all_says_so(self):
        shutil.rmtree(self.home / ".claude" / "projects")
        self.assertIn("No session transcripts", self.run_it())


if __name__ == "__main__":
    unittest.main()
