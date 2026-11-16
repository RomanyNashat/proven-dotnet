"""lessons_check.py on lessons files shaped like a real session's report, and on the items a hurried
capture gets wrong: no form, a rule where a test would hold, a lesson the harness already shows."""
import os
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).resolve().parents[2] / ".claude" / "skills" / "lessons" / "scripts" / "lessons_check.py"

READY = """# Lessons: handover for a two-week leave
Date: 2026-11-10 · Layers: proven-dotnet 0.3.38 · Where: Service A, Service B

## 1. Corrections
### L1. The parity test ran after the first edit
- **Did:** edited the mapper, then wrote the characterization test. **Wanted:** the test first.
- **Evidence:** "the test has to see the old code, not yours"
- **Form:** test
- **Harness change:** `tests/hooks/test_parity_order.py`: fails when an edit comes before the first test run
- **Priority:** must
- **Status:** open

### L2. Redis on the default port in a new service
- **Did:** wrote `:6379`. **Wanted:** the team's port.
- **Evidence:** "every time"
- **Form:** check
- **Harness change:** `known-traps` rule `redis-default-port`
- **Priority:** should
- **Status:** applied (v0.3.38)

### L3. Asked the team lead before the DBA
- **Did:** sent the schema question to the lead. **Wanted:** the DBA first, the lead copied.
- **Form:** rule
- **Harness change:** `skills/handover/SKILL.md` Gotchas: a schema question goes to the DBA first
- **Why not a test:** who to ask is judgement in a conversation; nothing in the code shows it
- **Priority:** nice
- **Status:** open

### L4. The handover skipped the journal
- **Did:** read only git. **Wanted:** the journal too.
- **Form:** none
- **Shown by:** `.claude/agents/handover-repo-analyst.md:24` already reads the journal first
- **Priority:** should
- **Status:** dropped (shown by the analyst's step 1)

## 2. Steps taught
### L5. Procedure: the deployment document
- **When:** before a release goes to production
- **Steps:** 1. compare the branches 2. fill the template 3. check the scripts
- **Harness change:** a section in `skills/deployment-document/SKILL.md`
- **Priority:** must
- **Status:** open

## 4. What worked — keep as is
- The per-repo analysts in parallel. See https://github.com/oasdiff/oasdiff and http://localhost:5000/health.

## 5. Open questions
- Should `/handover` default to Claude triaging on leave?
"""


def run(*paths, env=None):
    out = subprocess.run([sys.executable, str(SCRIPT), *map(str, paths)], capture_output=True, env=env)
    return out.returncode, out.stdout.decode("utf-8")


class Stories(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp(prefix="lessons "))

    def tearDown(self):
        shutil.rmtree(self.tmp, ignore_errors=True)

    def write(self, text, name="2026-11-10-handover.md"):
        path = self.tmp / name
        path.write_text(text, encoding="utf-8")
        return path

    def test_story_a_handover_report_with_each_lesson_in_its_cheapest_form_is_ready(self):
        code, out = run(self.write(READY))
        self.assertEqual((0, "lessons check: 5 item(s) in 1 file(s), 0 problem(s)"), (code, out.strip()))

    def test_story_a_hurried_capture_is_sent_back_item_by_item(self):
        # Given the same report written at the end of a long day
        text = (READY.replace("- **Form:** test\n", "")                                                     # L1: no form
                .replace("- **Why not a test:** who to ask is judgement in a conversation; nothing in the code shows it\n", "")  # L3
                .replace("`known-traps` rule `redis-default-port`", "a note in `skills/redis-patterns/SKILL.md`"))      # L2: check, but no check
        code, out = run(self.write(text))
        # Then each is named, with what to do
        self.assertEqual(1, code)
        self.assertIn("L1 (The parity test ran after the first edit): no Form; the cheapest that lasts: test, check, rule, prose, none", out)
        self.assertIn("L2 (Redis on the default port in a new service): Form check, but the harness change names no script", out)
        self.assertIn("L3 (Asked the team lead before the DBA): Form rule: say in 'Why not a test:'", out)

    def test_story_a_lesson_the_harness_already_shows_is_dropped_not_written_again(self):
        text = READY.replace("- **Status:** dropped (shown by the analyst's step 1)", "- **Status:** applied (v0.3.39)")
        self.assertIn("Form none writes nothing, so it can't be applied", run(self.write(text))[1])
        text = READY.replace("- **Shown by:** `.claude/agents/handover-repo-analyst.md:24` already reads the journal first\n", "- **Shown by:** it does that\n")
        self.assertIn("'Shown by:' names the file (and line)", run(self.write(text))[1])

    def test_a_test_form_whose_change_is_a_sentence(self):
        text = READY.replace("`tests/hooks/test_parity_order.py`: fails when an edit comes before the first test run",
                             "`skills/remediation-parity/SKILL.md`: say the test comes first")
        self.assertIn("Form test, but the harness change names no test file", run(self.write(text))[1])

    def test_secrets_and_internal_addresses_never_travel_to_the_harness_repo(self):
        text = READY + ("\n- Server=db-prod-01;Database=Visits;User Id=app;Password=x\n"
                        "- Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjMifQ.abc\n"
                        "- see http://jenkins.corp/job/visits and http://buildbox:8080/log\n")
        out = run(self.write(text))[1]
        for what in ("a connection string", "a token", "an internal address"):
            self.assertIn(what, out)
        self.assertNotIn("localhost", out)  # a local address is no secret

    def test_a_status_that_says_nothing(self):
        out = run(self.write(READY.replace("- **Status:** applied (v0.3.38)", "- **Status:** done")))[1]
        self.assertIn("status 'done' is open, applied (vX.Y.Z) or dropped (<reason>)", out)

    def test_several_files_at_once(self):
        a = self.write(READY, "a.md")
        b = self.write(READY.replace("- **Form:** test\n", ""), "b.md")
        code, out = run(a, b)
        self.assertEqual(1, code)
        self.assertIn("b.md:", out)
        self.assertNotIn("a.md:", out)
        self.assertIn("10 item(s) in 2 file(s), 1 problem(s)", out)


class Production(unittest.TestCase):
    def test_a_windows_report_with_arabic_evidence_on_a_cp1252_console(self):
        tmp = Path(tempfile.mkdtemp(prefix="lessons win "))
        try:
            text = READY.replace('"every time"', '"كل مرة نفس الغلطة"').replace(
                "`.claude/agents/handover-repo-analyst.md:24`", r"`.claude\agents\handover-repo-analyst.md:24`")
            path = tmp / "2026-11-10-تسليم.md"
            path.write_bytes("﻿".encode() + text.replace("\n", "\r\n").encode("utf-8"))
            code, out = run(path, env=dict(os.environ, PYTHONIOENCODING="cp1252"))
            self.assertEqual(0, code, out)
        finally:
            shutil.rmtree(tmp, ignore_errors=True)


if __name__ == "__main__":
    unittest.main()
