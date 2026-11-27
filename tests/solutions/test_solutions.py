"""solutions.py: a fixed problem written down in the service repo, so the next person (or session) finds it
before solving it again."""
import json
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).resolve().parents[2] / ".claude" / "skills" / "problem-analysis" / "scripts" / "solutions.py"


class Solutions(unittest.TestCase):
    def setUp(self):
        self.repo = Path(tempfile.mkdtemp(prefix="solutions "))

    def tearDown(self):
        shutil.rmtree(self.repo, ignore_errors=True)

    def run_it(self, *args):
        return subprocess.run([sys.executable, str(SCRIPT), *args, "--repo", str(self.repo)],
                              capture_output=True, text=True, encoding="utf-8")

    def write(self, **fields):
        note = {"title": "Redis times out on 6379 though the pod is healthy",
                "symptoms": "StackExchange.Redis timeout on start-up; the health check stays green",
                "cause": "the cluster serves Redis on port 80, and the connection string said 6379",
                "fix": "use <name>-redis:80,abortConnect=false",
                "tests": "tests/Cache.Tests/ConnectionTests.cs",
                "files": "src/Infrastructure/Cache/RedisSetup.cs", "tags": "redis, config", **fields}
        path = self.repo / "note.json"
        path.write_text(json.dumps(note), encoding="utf-8")
        return self.run_it("new", "--from", str(path))

    def test_story_a_fix_written_down_is_found_by_its_symptom_the_next_time(self):
        # Given last month's fix was written down
        made = self.write()
        self.assertEqual(0, made.returncode, made.stderr)
        # When someone meets the same timeout and searches by what they see
        found = self.run_it("find", "redis timeout on start-up")
        # Then the note comes first, with its cause and fix in the line
        self.assertEqual(0, found.returncode)
        first = found.stdout.splitlines()[0]
        self.assertIn("docs/solutions/", first)
        self.assertIn("Redis times out on 6379", first)

    def test_a_note_lands_in_docs_solutions_with_a_dated_name_and_its_fields(self):
        self.write()
        notes = list((self.repo / "docs" / "solutions").glob("*.md"))
        self.assertEqual(1, len(notes))
        self.assertRegex(notes[0].name, r"^\d{4}-\d{2}-\d{2}-redis-times-out-on-6379-though-the-pod-is-healthy\.md$")
        text = notes[0].read_text(encoding="utf-8")
        for heading in ("## Symptoms", "## Cause", "## Fix", "## Tests"):
            self.assertIn(heading, text)
        self.assertIn("files: [src/Infrastructure/Cache/RedisSetup.cs]", text)

    def test_a_note_without_a_cause_or_a_fix_is_refused(self):
        made = self.write(cause="")
        self.assertEqual(2, made.returncode)
        self.assertIn("cause", made.stderr)
        self.assertFalse((self.repo / "docs" / "solutions").exists())

    def test_long_numbers_and_emails_never_reach_a_note(self):
        self.write(symptoms="fails for national id 1012345678, reported by a.b@x.sa")
        text = next((self.repo / "docs" / "solutions").glob("*.md")).read_text(encoding="utf-8")
        self.assertIn("<number>", text)
        self.assertIn("<email>", text)
        self.assertNotIn("1012345678", text)

    def test_the_same_title_twice_keeps_both(self):
        self.write()
        self.write()
        self.assertEqual(2, len(list((self.repo / "docs" / "solutions").glob("*.md"))))

    def test_a_file_the_change_touches_finds_its_notes(self):
        self.write()
        found = self.run_it("find", "--file", "src\\Infrastructure\\Cache\\RedisSetup.cs")
        self.assertIn("Redis times out on 6379", found.stdout)

    def test_nothing_matching_says_so(self):
        self.write()
        found = self.run_it("find", "kafka offset lag")
        self.assertEqual(0, found.returncode)
        self.assertIn("No note in docs/solutions matches", found.stdout)

    def test_check_names_a_hand_written_note_missing_a_section(self):
        folder = self.repo / "docs" / "solutions"
        folder.mkdir(parents=True)
        (folder / "2026-11-01-x.md").write_text("---\ntitle: x\n---\n## Symptoms\ny\n", encoding="utf-8")
        checked = self.run_it("check")
        self.assertEqual(1, checked.returncode)
        self.assertIn("2026-11-01-x.md: no ## Cause", checked.stdout)


if __name__ == "__main__":
    unittest.main()
