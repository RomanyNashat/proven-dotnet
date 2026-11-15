"""story_check.py on a real-shaped story (fixture/story.md, cancelling a visit) and the code it touches
(fixture/src/Visits/CancelVisit.cs): each story is a way a story check goes wrong before anyone reads it."""
import json
import os
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parent
SCRIPT = HERE.parents[1] / ".claude" / "skills" / "story-check" / "scripts" / "story_check.py"
FIXTURE = HERE / "fixture"


def good_items():
    return [
        {"item": "empty", "verdict": "n/a", "reason": "a visit to cancel exists by definition"},
        {"item": "maximum", "verdict": "n/a", "reason": "one visit at a time"},
        {"item": "failure", "verdict": "gap", "question": "If the booking store is down, does the app say so or keep the visit shown as booked?"},
        {"item": "permission", "verdict": "code", "where": "src/Visits/CancelVisit.cs:8",
         "snippet": "visit.PatientId != patientId"},
        {"item": "duplicates", "verdict": "stated", "quote": "Cancelling a visit that is already cancelled changes nothing"},
        {"item": "interruption", "verdict": "n/a", "reason": "one request, nothing to resume"},
        {"item": "existing-data", "verdict": "gap", "question": "Visits booked through the old call centre have no patient id: may those be cancelled in the app?"},
        {"item": "language", "verdict": "stated", "quote": "shown in Arabic or English"},
        {"item": "touches", "verdict": "gap", "question": "Does the clinic's daily schedule export need to drop cancelled visits?"},
    ]


class Stories(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp(prefix="story check "))

    def tearDown(self):
        shutil.rmtree(self.tmp, ignore_errors=True)

    def run_check(self, items, story=FIXTURE / "story.md", repo=FIXTURE):
        report = self.tmp / "report.json"
        report.write_text(json.dumps({"items": items}, ensure_ascii=False), encoding="utf-8")
        return subprocess.run([sys.executable, str(SCRIPT), str(report), "--story", str(story), "--repo", str(repo)],
                              capture_output=True, text=True, encoding="utf-8")

    def replace(self, item, **fields):
        items = good_items()
        for i in items:
            if i["item"] == item:
                i.clear()
                i.update({"item": item, **fields})
        return items

    def test_story_a_grounded_check_rules_not_ready_with_its_questions_numbered(self):
        # Given a check where every settled case has its quote or its line of code
        result = self.run_check(good_items())
        # Then it shows, ruling first, the three questions to answer before building
        self.assertEqual(0, result.returncode, result.stdout)
        self.assertTrue(result.stdout.startswith("Story check: NOT READY (3 questions)\n"))
        self.assertIn("1. If the booking store is down, does the app say so or keep the visit shown as booked? (failure)", result.stdout)
        self.assertIn("| permission | the code: src/Visits/CancelVisit.cs:8 |", result.stdout)

    def test_story_a_quote_the_story_never_said_is_refused(self):
        # Given a check that claims the story settles duplicates with words it doesn't have
        items = self.replace("duplicates", verdict="stated", quote="A second cancel returns 409 Conflict")
        result = self.run_check(items)
        # Then it isn't shown
        self.assertEqual(1, result.returncode)
        self.assertIn("duplicates: the quote isn't in the story: \"A second cancel returns 409 Conflict\"", result.stdout)

    def test_story_code_that_doesnt_hold_the_claim_is_refused(self):
        items = self.replace("failure", verdict="code", where="src/Visits/CancelVisit.cs:12",
                             snippet="catch (TimeoutRejectedException")
        result = self.run_check(items)
        self.assertEqual(1, result.returncode)
        self.assertIn("failure: src/Visits/CancelVisit.cs:12 doesn't hold `catch (TimeoutRejectedException`", result.stdout)

    def test_a_line_two_off_still_counts(self):
        items = self.replace("permission", verdict="code", where="src/Visits/CancelVisit.cs:10",
                             snippet="visit.PatientId != patientId")
        self.assertEqual(0, self.run_check(items).returncode)

    def test_a_file_that_isnt_there_is_refused(self):
        items = self.replace("permission", verdict="code", where="src/Visits/Policy.cs:3", snippet="Owner")
        self.assertIn("permission: src/Visits/Policy.cs doesn't exist", self.run_check(items).stdout)

    def test_story_a_question_the_story_answers_is_refused_naming_the_sentence(self):
        # Given a check that asks what the story already says
        items = self.replace("maximum", verdict="gap", question="How long before it starts can a patient cancel their visit?")
        result = self.run_check(items)
        # Then it names the sentence that answers it
        self.assertEqual(1, result.returncode)
        self.assertIn("maximum: the story may already answer this: \"AC1: A patient can cancel their own booked visit up to 2 "
                      "hours before it starts.\"", result.stdout)

    def test_the_same_question_with_a_reason_the_sentence_doesnt_answer_it_is_kept(self):
        items = self.replace("maximum", verdict="gap", question="How long before it starts can a patient cancel their visit?",
                             not_answered_because="AC1 says 2 hours, but not whose clock: the clinic's or the patient's")
        self.assertEqual(0, self.run_check(items).returncode)

    def test_a_routine_item_left_out_is_refused(self):
        items = [i for i in good_items() if i["item"] != "interruption"]
        self.assertIn("interruption: no entry", self.run_check(items).stdout)

    def test_gap_without_a_question_and_na_without_a_reason(self):
        items = self.replace("touches", verdict="gap")
        items = [dict(i, reason="") if i["item"] == "empty" else i for i in items]
        out = self.run_check(items).stdout
        self.assertIn("touches: a gap needs the question to ask", out)
        self.assertIn("empty: n/a needs a reason", out)

    def test_story_a_team_case_is_required_once_its_list_is_given(self):
        # Given a team whose stories must also answer consent, listed in its own file
        team = self.tmp / "items.txt"
        team.write_text("# our cases\nconsent: whose consent the action needs\n\n", encoding="utf-8")
        report = self.tmp / "report.json"
        report.write_text(json.dumps({"items": good_items()}), encoding="utf-8")
        run = lambda: subprocess.run([sys.executable, str(SCRIPT), str(report), "--story", str(FIXTURE / "story.md"),
                                      "--repo", str(FIXTURE), "--items", str(team)], capture_output=True, text=True, encoding="utf-8")
        # Then a report that leaves it out is refused, naming it
        first = run()
        self.assertEqual(1, first.returncode)
        self.assertIn("consent: no entry (whose consent the action needs)", first.stdout)
        # And once it has an entry, checked like the others, the report passes
        report.write_text(json.dumps({"items": good_items() + [{"item": "consent", "verdict": "gap",
                                      "question": "Does cancelling need the patient's consent, or only the booking's owner?"}]}), encoding="utf-8")
        second = run()
        self.assertEqual(0, second.returncode, second.stdout)
        self.assertIn("NOT READY (4 questions)", second.stdout)

    def test_everything_settled_is_ready(self):
        items = [i if i["verdict"] != "gap" else {"item": i["item"], "verdict": "n/a", "reason": "settled with the lead"}
                 for i in good_items()]
        result = self.run_check(items)
        self.assertTrue(result.stdout.startswith("Story check: READY\n"), result.stdout)


# The console a Windows developer runs this in is cp1252: the Arabic story must still come out whole.
WINDOWS_CONSOLE = dict(os.environ, PYTHONIOENCODING="cp1252")


class Production(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp(prefix="story check prod "))

    def tearDown(self):
        shutil.rmtree(self.tmp, ignore_errors=True)

    def test_an_arabic_story_from_a_windows_checkout(self):
        # A story pasted from Jira in Arabic, with diacritics and the alef with hamza, saved with CRLF and a BOM;
        # the check quotes it without them and cites the code with a Windows path
        story = self.tmp / "story.md"
        story.write_bytes("﻿# إلغاء موعد\r\n\r\n- AC1: يُمكن للمريض إلغاء موعده قبل ساعتين من بدايته.\r\n".encode("utf-8"))
        repo = self.tmp / "repo"
        shutil.copytree(FIXTURE / "src", repo / "src")
        items = good_items()
        items[4] = {"item": "duplicates", "verdict": "stated", "quote": "يمكن للمريض الغاء موعده"}
        items[7] = {"item": "language", "verdict": "gap", "question": "Is the confirmation shown in English too?"}
        items[3]["where"] = "src\\Visits\\CancelVisit.cs:8"
        report = self.tmp / "report.json"
        report.write_bytes("﻿".encode() + json.dumps({"items": items}, ensure_ascii=False).encode("utf-8"))
        out = subprocess.run([sys.executable, str(SCRIPT), str(report), "--story", str(story), "--repo", str(repo)],
                             capture_output=True, text=True, encoding="utf-8", env=WINDOWS_CONSOLE)
        self.assertEqual(0, out.returncode, out.stdout + out.stderr)
        self.assertIn("NOT READY (4 questions)", out.stdout)

    def test_an_arabic_question_the_story_answers(self):
        story = self.tmp / "story.md"
        story.write_text("- AC1: يمكن للمريض إلغاء موعده قبل ساعتين من بدايته.\n", encoding="utf-8")
        items = good_items()
        items[1] = {"item": "maximum", "verdict": "gap", "question": "هل يمكن للمريض إلغاء موعده قبل ساعتين؟"}
        report = self.tmp / "report.json"
        report.write_text(json.dumps({"items": items}, ensure_ascii=False), encoding="utf-8")
        out = subprocess.run([sys.executable, str(SCRIPT), str(report), "--story", str(story), "--repo", str(FIXTURE)],
                             capture_output=True, text=True, encoding="utf-8", env=WINDOWS_CONSOLE)
        self.assertEqual(1, out.returncode)
        self.assertIn("maximum: the story may already answer this: \"AC1: يمكن للمريض إلغاء موعده قبل ساعتين من بدايته.\"", out.stdout)


if __name__ == "__main__":
    unittest.main()
