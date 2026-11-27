"""plan_check.py: a plan is checked before any code — scope against the story, design against what the repo
already has, and the API as its callers will see it. The story and code are story-check's fixture."""
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parent
SCRIPT = HERE.parents[1] / ".claude" / "skills" / "story-check" / "scripts" / "plan_check.py"
FIXTURE = HERE.parent / "storycheck" / "fixture"

GOOD = """# Implementation Plan: Cancel a booked visit

## Summary
Patients cancel their own visit up to 2 hours before it starts.

## Tasks

### Task 1: Cancel through the handler (2 / 3 / 5 h)
**Serves**: AC1, AC3
**Agent**: qa-engineer, then tdd-guide

### Task 2: Free the slot (1 / 2 / 4 h)
**Serves**: AC2

### Task 3: Confirmation text in both languages (1 / 1 / 2 h)
**Serves**: AC4

### Task 4: Index for the slot lookup (1 / 1 / 2 h)
**Serves**: enabler: the slot query needs it once visits pass a million rows

## Builds on
- uses: src/Visits/CancelVisit.cs:7 `store.FindAsync(visitId, ct)` — the visit is already loaded by id
- uses: src/Visits/CancelVisit.cs:8 `visit.PatientId != patientId` — the ownership check is already there
- new: table SlotRelease — because the store keeps no history of freed slots, and the clinic report needs one

## API for its callers
- POST /api/v1/visits/{id:int:min(1)}/cancel
```json
{"visitId": 42, "status": "Cancelled"}
```
- errors: 404 when the visit isn't the caller's, 409 when it starts in under 2 hours
- old app versions: a new endpoint; old versions never call it and see no change
"""


class PlanCheck(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp(prefix="plan check "))

    def tearDown(self):
        shutil.rmtree(self.tmp, ignore_errors=True)

    def check(self, plan, story=FIXTURE / "story.md"):
        path = self.tmp / "plan.md"
        path.write_text(plan, encoding="utf-8")
        args = [sys.executable, str(SCRIPT), str(path), "--repo", str(FIXTURE)] + (["--story", str(story)] if story else [])
        return subprocess.run(args, capture_output=True, text=True, encoding="utf-8")

    def test_story_a_plan_that_covers_the_story_and_cites_the_code_is_ready(self):
        done = self.check(GOOD)
        self.assertEqual(0, done.returncode, done.stdout)
        self.assertTrue(done.stdout.startswith("Plan check: READY"), done.stdout)

    def test_story_a_task_beyond_the_story_is_named_before_any_code(self):
        # Given the planner added a task no criterion asks for
        plan = GOOD.replace("### Task 4: Index for the slot lookup (1 / 1 / 2 h)\n**Serves**: enabler: the slot query needs it once visits pass a million rows",
                            "### Task 4: Let clinics cancel too (3 / 5 / 8 h)\n**Agent**: tdd-guide")
        done = self.check(plan)
        # Then the check stops it, naming the task
        self.assertEqual(1, done.returncode)
        self.assertIn("Task 4 serves no criterion", done.stdout)

    def test_a_criterion_no_task_serves_is_a_gap(self):
        done = self.check(GOOD.replace("**Serves**: AC4", "**Serves**: AC1"))
        self.assertEqual(1, done.returncode)
        self.assertIn("AC4 has no task", done.stdout)

    def test_a_task_naming_a_criterion_the_story_lacks_is_refused(self):
        done = self.check(GOOD.replace("**Serves**: AC2", "**Serves**: AC2, AC7"))
        self.assertIn("Task 2 names AC7, which the story doesn't have", done.stdout)

    def test_story_a_new_table_for_what_the_repo_already_has_is_named_where_it_is(self):
        # Given the plan adds a class the service already has
        plan = GOOD.replace("- new: table SlotRelease — because the store keeps no history of freed slots, and the clinic report needs one",
                            "- new: class CancelVisit — because we need a handler")
        done = self.check(plan)
        # Then the check points at the one already there
        self.assertEqual(1, done.returncode)
        self.assertIn("CancelVisit already exists at src/Visits/CancelVisit.cs:3", done.stdout)

    def test_a_new_thing_without_a_reason_is_refused(self):
        done = self.check(GOOD.replace(" — because the store keeps no history of freed slots, and the clinic report needs one", ""))
        self.assertIn("SlotRelease is new with no reason", done.stdout)

    def test_a_citation_the_code_doesnt_hold_is_refused(self):
        done = self.check(GOOD.replace("`visit.PatientId != patientId`", "`visit.OwnerId != patientId`"))
        self.assertEqual(1, done.returncode)
        self.assertIn("src/Visits/CancelVisit.cs:8 doesn't hold `visit.OwnerId != patientId`", done.stdout)

    def test_a_plan_with_no_builds_on_says_nothing_of_what_exists(self):
        start = GOOD.index("## Builds on")
        end = GOOD.index("## API for its callers")
        done = self.check(GOOD[:start] + GOOD[end:])
        self.assertIn("no `## Builds on`", done.stdout)

    def test_story_an_endpoint_the_mobile_team_couldnt_read_from_the_plan_is_named(self):
        # Given a plan that adds an endpoint and says nothing of its errors or the old app versions
        plan = GOOD.replace("- errors: 404 when the visit isn't the caller's, 409 when it starts in under 2 hours\n", "")
        plan = plan.replace("- old app versions: a new endpoint; old versions never call it and see no change\n", "")
        done = self.check(plan)
        self.assertEqual(1, done.returncode)
        self.assertIn("the API section has no errors", done.stdout)
        self.assertIn("what old app versions see", done.stdout)

    def test_an_endpoint_with_no_api_section_at_all_is_named(self):
        start = GOOD.index("## API for its callers")
        done = self.check(GOOD[:start] + "\nTask 1 adds POST /api/v1/visits/{id}/cancel.\n")
        self.assertIn("no `## API for its callers`", done.stdout)

    def test_a_plan_with_no_endpoint_needs_no_api_section(self):
        start = GOOD.index("## API for its callers")
        done = self.check(GOOD[:start])
        self.assertEqual(0, done.returncode, done.stdout)

    def test_without_a_story_the_scope_check_is_skipped_and_said(self):
        done = self.check(GOOD, story=None)
        self.assertEqual(0, done.returncode, done.stdout)
        self.assertIn("no story: scope not checked", done.stdout)

    def test_windows_line_endings_read_the_same(self):
        done = self.check(GOOD.replace("\n", "\r\n"))
        self.assertEqual(0, done.returncode, done.stdout)


if __name__ == "__main__":
    unittest.main()
